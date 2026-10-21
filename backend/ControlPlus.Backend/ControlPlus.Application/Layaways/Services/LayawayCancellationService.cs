using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Layaways.Services;

public sealed class LayawayCancellationService(
    ILayawayCancellationRepository layaways, ILayawayCreateRepository creation,
    ISalesRepository sales, ICashRepository cash, IUserRepository users,
    IPermissionChecker permissions, IPermissionRepository permissionRepository,
    IPasswordHasher passwordHasher, ILoginAttemptCoordinator loginAttempts,
    IAuditRepository audits, IUnitOfWork unitOfWork, IClock clock)
    : ILayawayCancellationService
{
    public Task<Result<LayawayCancellationDto>> CancelAsync(ActorContext actor, Guid layawayId,
        string idempotencyKey, CancelLayawayRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = Normalize(layawayId, idempotencyKey, request);
        if (input.Error is not null) return Task.FromResult(Fail(input.Error));
        return loginAttempts.ExecuteAsync(input.AuthorizerName!,
            token => CancelWithinTransactionAsync(actor, layawayId, request, input, token),
            cancellationToken);
    }

    private async Task<Result<LayawayCancellationDto>> CancelWithinTransactionAsync(
        ActorContext actor, Guid layawayId, CancelLayawayRequest request,
        NormalizedCancellation input, CancellationToken cancellationToken)
    {
        // The login coordinator owns the transaction and serializes password attempts.
        // The installation lock serializes cancellation with payments and delivery.
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Fail(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Fail(ApplicationError.Unauthorized());
        if (snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.LayawayCancel, StringComparer.OrdinalIgnoreCase) ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.LayawayRefundDecide, StringComparer.OrdinalIgnoreCase))
            return Fail(ApplicationError.Forbidden());

        var previous = await sales.GetIdempotencyAsync(installation.Id, input.Key!, cancellationToken);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "ANULAR_APARTADO" ||
                previous.HashSolicitud != input.Hash || string.IsNullOrEmpty(previous.Resultado))
                return Fail(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var replay = JsonSerializer.Deserialize<LayawayCancellationDto>(previous.Resultado);
            return replay is null ? Fail(ApplicationError.Conflict("La operación idempotente no tiene resultado recuperable."))
                : Result.Success(replay);
        }

        var terminal = await sales.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Fail(ApplicationError.NotFound("la terminal activa"));
        TurnoCaja? shift = null;
        SesionOperador? session = null;
        if (input.Amount > 0)
        {
            shift = await sales.GetOpenShiftForUpdateAsync(cancellationToken);
            if (shift is null || shift.TerminalId != terminal.Id)
                return Fail(ApplicationError.Conflict("Debe existir un turno abierto en la terminal activa."));
            if (shift.ModoOperacion == CashConstants.IndividualMode)
            {
                if (shift.UsuarioResponsableId != actor.UserId) return Fail(ApplicationError.Forbidden());
            }
            else if (shift.ModoOperacion == CashConstants.SharedMode)
            {
                session = await creation.GetActiveOperatorSessionForUpdateAsync(
                    shift.Id, terminal.Id, actor.UserId, cancellationToken);
                if (session is null)
                    return Fail(ApplicationError.Conflict("Se requiere una sesión activa del operador."));
            }
            else return Fail(ApplicationError.Conflict("La modalidad del turno no es válida."));
        }

        var layaway = await layaways.GetForUpdateAsync(layawayId, cancellationToken);
        if (layaway is null || layaway.InstalacionId != installation.Id)
            return Fail(ApplicationError.NotFound("el apartado"));
        if (layaway.Version != request.ExpectedVersion)
            return Fail(ApplicationError.ConcurrencyConflict("El apartado fue modificado. Recárguelo."));
        if (layaway.Estado is not ("ACTIVO" or "PAGADO"))
            return Fail(ApplicationError.Conflict("El apartado no admite cancelación."));
        var paid = await layaways.GetTotalPaidAsync(layaway.Id, cancellationToken);
        if (!CashMoney.IsValid(paid, allowNegative: false) || input.Amount > paid)
            return Fail(ApplicationError.Validation("La devolución no puede superar el total efectivamente abonado."));
        if (request.ReasonId is Guid reasonId &&
            await layaways.GetReasonAsync(installation.EstablecimientoId, reasonId, cancellationToken) is null)
            return Fail(ApplicationError.Validation("El motivo de cancelación no está activo."));

        var methods = input.Amount == 0
            ? []
            : await sales.GetPaymentMethodsAsync(input.Refunds!.Select(x => x.MethodCode).ToArray(), cancellationToken);
        if (methods.Count != input.Refunds!.Length || methods.Any(x => !x.Activo) ||
            methods.Any(x => x.Codigo == "EFECTIVO" ? !x.AfectaEfectivo : x.AfectaEfectivo) ||
            input.Refunds.Any(x => methods.All(method => method.Codigo != x.MethodCode ||
                (method.RequiereReferencia && x.Reference is null))))
            return Fail(ApplicationError.Validation("Los medios de reembolso deben estar activos y conservar su clasificación."));

        if (shift is not null)
        {
            var expected = await cash.GetExpectedAmountsAsync(shift.Id, cancellationToken);
            var cashRefund = input.Refunds.Where(x => x.MethodCode == "EFECTIVO").Sum(x => x.Amount);
            try
            {
                var cashAvailable = CashMoney.Sum(expected.Where(x => x.AffectsCash)
                    .Select(x => x.ExpectedAmount));
                if (cashRefund > cashAvailable)
                    return Fail(ApplicationError.Conflict("El efectivo esperado no cubre la devolución."));
                CashMoney.Require(CashMoney.Subtract(cashAvailable, cashRefund), allowNegative: false);
                foreach (var refund in input.Refunds)
                {
                    var method = methods.Single(x => x.Codigo == refund.MethodCode);
                    var current = expected.Single(x => x.PaymentMethodId == method.Id).ExpectedAmount;
                    CashMoney.Subtract(current, refund.Amount);
                }
                CashMoney.Subtract(CashMoney.Sum(expected.Select(x => x.ExpectedAmount)), input.Amount);
            }
            catch (Exception ex) when (ex is CashMoneyException or OverflowException or InvalidOperationException)
            {
                return Fail(ApplicationError.Validation("El saldo de Caja excede el rango permitido."));
            }
        }

        var candidate = await users.GetByUserNameAsync(input.AuthorizerName!, cancellationToken);
        if (candidate is null) return Fail(ApplicationError.Forbidden());
        var authorizer = await sales.GetUserForUpdateAsync(candidate.Id, cancellationToken);
        if (authorizer is null || !authorizer.IsActive || authorizer.Id == actor.UserId ||
            authorizer.HighestRoleLevel < RoleLevel.Supervisor)
            return Fail(ApplicationError.Forbidden());
        if (!passwordHasher.Verify(request.Authorization.Password, authorizer.PasswordHash))
        {
            var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
            await users.UpdateAsync(authorizer, cancellationToken);
            if (locked)
                await AuditWriter.WriteAsync(audits, clock, authorizer.Id, AuditAction.UserLocked,
                    nameof(Usuario), authorizer.Id,
                    new { authorizer.Id, authorizer.FailedLoginAttempts }, cancellationToken);
            await AuditWriter.WriteCashFailureAsync(audits, clock, actor.UserId,
                AuditAction.LayawayAuthorizationRejected, nameof(Apartado), layaway.Id,
                new { Reason = "INVALID_AUTHORIZER_CREDENTIALS" }, Guid.CreateVersion7(),
                terminal.Id, null, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Fail(ApplicationError.Forbidden());
        }
        if (!await permissions.HasPermissionAsync(authorizer.Id, PermissionCodes.LayawayCancel,
                cancellationToken)) return Fail(ApplicationError.Forbidden());
        var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.LayawayCancel,
            cancellationToken);
        if (permission is null) return Fail(ApplicationError.Conflict("El permiso de cancelación no existe."));

        var lines = await layaways.GetLinesAsync(layaway.Id, cancellationToken);
        if (lines.Count == 0 || lines.Any(x => x.ProductoId == Guid.Empty || x.Cantidad <= 0))
            return Fail(ApplicationError.Conflict("El detalle del apartado no permite liberar la reserva."));
        var grouped = lines.GroupBy(x => x.ProductoId).OrderBy(x => x.Key).ToArray();
        var quantities = grouped.ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Cantidad));
        if (quantities.Values.Any(x => x > int.MaxValue))
            return Fail(ApplicationError.Conflict("La reserva excede el rango admitido."));
        var products = await sales.GetProductsForUpdateAsync(quantities.Keys.ToArray(), cancellationToken);
        if (products.Count != quantities.Count || products.Any(x =>
                x.StockReservado < quantities[x.Id] || x.StockReservado > x.StockActual))
            return Fail(ApplicationError.Conflict("El stock reservado no permite cancelar el apartado."));

        var timestamp = clock.UtcNow;
        var now = new DateTimeOffset(timestamp.UtcTicks - timestamp.UtcTicks % 10, TimeSpan.Zero);
        var correlationId = Guid.CreateVersion7();
        var movements = products.OrderBy(x => x.Id).Select(product =>
        {
            var reservedBefore = product.StockReservado;
            product.StockReservado -= (int)quantities[product.Id];
            return new MovimientoInventario
            {
                Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actor.UserId,
                ApartadoId = layaway.Id, TipoMovimiento = "LIBERACION_APARTADO",
                CantidadStock = 0, CantidadReservada = -(int)quantities[product.Id],
                StockAnterior = product.StockActual, StockPosterior = product.StockActual,
                ReservadoAnterior = reservedBefore, ReservadoPosterior = product.StockReservado,
                CostoPromedioAnterior = product.CostoPromedio, CostoPromedioPosterior = product.CostoPromedio,
                FechaHora = now.UtcDateTime, CorrelacionId = correlationId
            };
        }).ToArray();
        MovimientoCaja? cashMovement = null;
        if (shift is not null)
            cashMovement = MovimientoCaja.Create(shift.Id, actor.UserId, session?.Id,
                CashConstants.ExpenseMovement, "REEMBOLSO_APARTADO", input.Amount,
                $"Reembolso de apartado {layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}",
                correlationId, now);
        var byCode = methods.ToDictionary(x => x.Codigo);
        var refunds = input.Refunds.Select(x => new ReembolsoApartado
        {
            Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, TurnoCajaId = shift!.Id,
            MetodoPagoId = byCode[x.MethodCode].Id, UsuarioId = actor.UserId,
            SesionOperadorId = session?.Id, MovimientoCajaId = cashMovement!.Id,
            Valor = x.Amount, Referencia = x.Reference, FechaHora = now.UtcDateTime,
            CorrelacionId = correlationId
        }).ToArray();
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.Id, PermisoId = permission.Id,
            TipoOperacion = "ANULAR_APARTADO", CorrelacionId = correlationId,
            DescripcionOperacion = "Autorización de cancelación de apartado.",
            MetodoAutenticacion = "PASSWORD", Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime,
            FechaUtilizacion = now.UtcDateTime
        };
        layaway.Estado = "ANULADO";
        layaway.FechaAnulacion = now.UtcDateTime;
        layaway.UsuarioAnulacionId = actor.UserId;
        layaway.AutorizacionAnulacionId = authorization.Id;
        layaway.MotivoAnulacionId = request.ReasonId;
        layaway.ObservacionAnulacion = input.Observation;
        layaway.TotalAbonadoCancelacion = paid;
        layaway.ValorDevueltoCancelacion = input.Amount;
        layaway.ValorRetenidoCancelacion = paid - input.Amount;
        var response = new LayawayCancellationDto(layaway.Id,
            $"{layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}", layaway.Estado,
            layaway.Version + 1, paid, input.Amount, paid - input.Amount, now,
            actor.UserId, authorizer.Id, request.ReasonId, input.Observation, correlationId,
            refunds.Select(x => new LayawayRefundDto(x.Id,
                methods.Single(m => m.Id == x.MetodoPagoId).Codigo,
                x.Valor, x.Referencia, now)).ToArray());

        authorizer.RegisterSuccessfulLogin(now);
        await users.UpdateAsync(authorizer, cancellationToken);
        await sales.AddAuthorizationAsync(authorization, cancellationToken);
        await sales.AddInventoryMovementsAsync(movements, cancellationToken);
        if (cashMovement is not null) await sales.AddCashMovementAsync(cashMovement, cancellationToken);
        if (refunds.Length > 0) await layaways.AddRefundsAsync(refunds, cancellationToken);
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = session?.Id,
            ClaveIdempotencia = input.Key!, TipoOperacion = "ANULAR_APARTADO",
            HashSolicitud = input.Hash!, Entidad = nameof(Apartado), EntidadId = layaway.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlationId,
            FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.LayawayCancelled, nameof(Apartado), layaway.Id,
            new { layaway.Id, layaway.Estado, Paid = paid, Refunded = input.Amount,
                Retained = paid - input.Amount, RefundIds = refunds.Select(x => x.Id).ToArray(),
                InventoryMovementIds = movements.Select(x => x.Id).ToArray() },
            correlationId, authorizer.Id, session?.Id, terminal.Id, input.Observation,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (session is not null)
        {
            session.Close(now, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
                AuditAction.OperatorSessionClosed, nameof(SesionOperador), session.Id,
                new { session.Estado, LayawayId = layaway.Id, SessionConsumedByLayawayCancellation = true },
                correlationId, null, session.Id, terminal.Id, null, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(response);
    }

    private static NormalizedCancellation Normalize(Guid layawayId, string key,
        CancelLayawayRequest request)
    {
        if (layawayId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100 ||
            request is null || request.ExpectedVersion <= 0 || request.Authorization is null ||
            string.IsNullOrWhiteSpace(request.Authorization.UserName) ||
            string.IsNullOrWhiteSpace(request.Authorization.Password) ||
            request.Refunds is null || !CashMoney.IsValid(request.RefundAmount, allowNegative: false))
            return NormalizedCancellation.Fail("Clave, versión, importe, reembolsos y autorización son obligatorios.");
        var observation = string.IsNullOrWhiteSpace(request.Observation) ? null : request.Observation.Trim();
        if (observation?.Length > 1000 || request.Authorization.UserName.Trim().Length > 100 ||
            request.Refunds.Count > 3 ||
            (request.RefundAmount == 0 && request.Refunds.Count != 0) ||
            (request.RefundAmount > 0 && request.Refunds.Count == 0))
            return NormalizedCancellation.Fail("Importe, observación o cantidad de medios inválidos.");
        var refunds = new List<LayawayRefundRequest>();
        foreach (var refund in request.Refunds)
        {
            if (refund is null) return NormalizedCancellation.Fail("El reembolso es obligatorio.");
            var code = refund.MethodCode?.Trim().ToUpperInvariant();
            var reference = string.IsNullOrWhiteSpace(refund.Reference) ? null : refund.Reference.Trim();
            if (code is not "EFECTIVO" and not "TRANSFERENCIA" and not "NEQUI" ||
                !CashMoney.IsValid(refund.Amount, allowNegative: false) || refund.Amount <= 0 ||
                reference?.Length > 200 || (code == "EFECTIVO" ? reference is not null : reference is null))
                return NormalizedCancellation.Fail("Método, importe o referencia de reembolso inválidos.");
            refunds.Add(new LayawayRefundRequest(code, refund.Amount, reference));
        }
        if (refunds.Select(x => x.MethodCode).Distinct().Count() != refunds.Count)
            return NormalizedCancellation.Fail("No se puede repetir un medio de reembolso.");
        try
        {
            if (CashMoney.Sum(refunds.Select(x => x.Amount)) != request.RefundAmount)
                return NormalizedCancellation.Fail("Los reembolsos deben sumar exactamente el importe decidido.");
        }
        catch (Exception ex) when (ex is CashMoneyException or OverflowException)
        {
            return NormalizedCancellation.Fail("El importe acumulado excede el rango permitido.");
        }
        var sorted = refunds.OrderBy(x => x.MethodCode).ToArray();
        var name = request.Authorization.UserName.Trim().ToUpperInvariant();
        var canonical = JsonSerializer.Serialize(new { layawayId, request.ExpectedVersion,
            request.RefundAmount, Refunds = sorted, request.ReasonId, Observation = observation,
            AuthorizerUserName = name });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new NormalizedCancellation(key.Trim(), sorted, request.RefundAmount,
            observation, name, hash, null);
    }

    private static Result<LayawayCancellationDto> Fail(ApplicationError error) =>
        Result.Failure<LayawayCancellationDto>(error);

    private sealed record NormalizedCancellation(string? Key, LayawayRefundRequest[]? Refunds,
        decimal Amount, string? Observation, string? AuthorizerName, string? Hash,
        ApplicationError? Error)
    {
        public static NormalizedCancellation Fail(string message) =>
            new(null, null, 0, null, null, null, ApplicationError.Validation(message));
    }
}
