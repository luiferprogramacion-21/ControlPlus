using ControlPlus.Domain.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    private static readonly (string Code, string Name)[] SaleCancellationCatalog =
        ApprovedReasonCatalogs.SaleCancellation;

    public async Task<Result<IReadOnlyCollection<SaleCancellationReasonDto>>> ListCancellationReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        if (!await CanVoidAsync(actor.UserId, cancellationToken))
            return Result.Failure<IReadOnlyCollection<SaleCancellationReasonDto>>(ApplicationError.Forbidden());
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<IReadOnlyCollection<SaleCancellationReasonDto>>(ApplicationError.NotFound("la instalación activa"));
        var reasons = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId,
            "ANULACION_VENTA", cancellationToken);
        var paymentReasons = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId,
            "ANULACION_PAGO", cancellationToken);
        if (SaleCancellationCatalog.Any(c => !reasons.Any(r => r.Codigo == c.Code) ||
                                             !paymentReasons.Any(r => r.Codigo == c.Code)))
        {
            // First use: the approved reasons that do not exist yet are created automatically. If the catalog
            // cannot be completed (an incompatible code), the existing approved reasons are listed as before.
            var completed = await InitializeCancellationReasonsCoreAsync(actor, requireVoidPermission: false,
                cancellationToken);
            if (completed.IsSuccess) return completed;
        }
        return Result.Success<IReadOnlyCollection<SaleCancellationReasonDto>>(reasons
            .Where(IsApprovedSaleReason).Select(ToCancellationReason).ToArray());
    }

    public Task<Result<IReadOnlyCollection<SaleCancellationReasonDto>>> InitializeCancellationReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default) =>
        InitializeCancellationReasonsCoreAsync(actor, requireVoidPermission: true, cancellationToken);

    private async Task<Result<IReadOnlyCollection<SaleCancellationReasonDto>>> InitializeCancellationReasonsCoreAsync(
        ActorContext actor, bool requireVoidPermission, CancellationToken cancellationToken)
    {
        await using var transaction = await transactionManager.BeginAsync(cancellationToken: cancellationToken);
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<IReadOnlyCollection<SaleCancellationReasonDto>>(ApplicationError.NotFound("la instalación activa"));
        if (requireVoidPermission && !await CanVoidAsync(actor.UserId, cancellationToken))
            return Result.Failure<IReadOnlyCollection<SaleCancellationReasonDto>>(ApplicationError.Forbidden());
        var existing = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId,
            "ANULACION_VENTA", cancellationToken);
        var existingPayments = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId,
            "ANULACION_PAGO", cancellationToken);
        var created = new List<MotivoOperacion>();
        foreach (var operationType in new[] { "ANULACION_VENTA", "ANULACION_PAGO" })
        for (var index = 0; index < SaleCancellationCatalog.Length; index++)
        {
            var (code, name) = SaleCancellationCatalog[index];
            var found = (operationType == "ANULACION_VENTA" ? existing : existingPayments)
                .SingleOrDefault(x => x.Codigo == code);
            if (found is not null)
            {
                if (!IsApprovedReason(found, operationType))
                    return Result.Failure<IReadOnlyCollection<SaleCancellationReasonDto>>(
                        ApplicationError.Conflict("El catálogo de anulación de ventas contiene un código incompatible."));
                continue;
            }
            created.Add(new MotivoOperacion
            {
                Id = Guid.CreateVersion7(), EstablecimientoId = installation.EstablecimientoId,
                TipoOperacion = operationType, Codigo = code, Nombre = name,
                Descripcion = name, OrdenVisual = (index + 1) * 10, Activo = true,
                FechaCreacion = clock.UtcNow.UtcDateTime, Version = 1
            });
        }
        if (created.Count > 0)
        {
            await repository.AddCancellationReasonsAsync(created, cancellationToken);
            await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
                AuditAction.SaleCancellationReasonsInitialized, nameof(MotivoOperacion), null,
                new { Codes = created.Select(x => x.Codigo).ToArray() }, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<SaleCancellationReasonDto>>(existing.Concat(created)
            .Where(IsApprovedSaleReason).OrderBy(x => x.OrdenVisual)
            .Select(ToCancellationReason).ToArray());
    }

    public async Task<Result<CancelSaleDto>> CancelAsync(ActorContext actor, Guid saleId,
        string idempotencyKey, CancelSaleRequest request, CancellationToken cancellationToken = default)
    {
        if (!await CanVoidAsync(actor.UserId, cancellationToken))
            return Result.Failure<CancelSaleDto>(ApplicationError.Forbidden());
        var key = idempotencyKey?.Trim() ?? "";
        if (saleId == Guid.Empty || request is null || request.ReasonId == Guid.Empty ||
            request.ExpectedVersion <= 0 || key.Length is < 1 or > 100 ||
            key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')) ||
            request.Observation?.Length > 500)
            return Result.Failure<CancelSaleDto>(Validation("La solicitud de anulación o Idempotency-Key es inválida."));
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<CancelSaleDto>(ApplicationError.ConcurrencyConflict("La versión de la venta está agotada."));
        var scanned = await ResolveScannedAuthorizationAsync(request.Authorization, cancellationToken);
        if (scanned.Error is not null) return Result.Failure<CancelSaleDto>(scanned.Error);
        request = request with { Authorization = scanned.Authorization };
        var observation = string.IsNullOrWhiteSpace(request.Observation) ? null : request.Observation.Trim();
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            SaleId = saleId, request.ExpectedVersion, request.ReasonId,
            Observation = observation, Actor = actor.UserId
        }));
        var account = string.IsNullOrWhiteSpace(request.Authorization?.UserName)
            ? $"SALE-CANCEL-REPLAY:{actor.UserId:N}"
            : request.Authorization.UserName.Trim().ToUpperInvariant();
        return await loginAttemptCoordinator.ExecuteAsync(account,
            token => CancelWithinTransactionAsync(actor, saleId, key, request, observation, fingerprint, token),
            cancellationToken);
    }

    private async Task<Result<CancelSaleDto>> CancelWithinTransactionAsync(ActorContext actor,
        Guid saleId, string key, CancelSaleRequest request, string? observation, string fingerprint,
        CancellationToken ct)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<CancelSaleDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await CanVoidAsync(actor.UserId, ct))
            return Result.Failure<CancelSaleDto>(ApplicationError.Forbidden());
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "ANULAR_VENTA" ||
                previous.HashSolicitud != fingerprint || previous.EntidadId != saleId || previous.Resultado is null)
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            CancelSaleDto? saved;
            try { saved = JsonSerializer.Deserialize<CancelSaleDto>(previous.Resultado); }
            catch (JsonException) { saved = null; }
            return saved is null
                ? Result.Failure<CancelSaleDto>(ApplicationError.Conflict("La anulación original no es recuperable."))
                : Result.Success(saved);
        }
        var actorSnapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        var administratorSelfAuthorized = actorSnapshot?.HighestRoleLevel == RoleLevel.Administrador &&
            request.Authorization is null;
        if (!administratorSelfAuthorized &&
            (request.Authorization is null || !request.Authorization.IsComplete()))
            return Result.Failure<CancelSaleDto>(Validation("Las credenciales de otro Supervisor o Administrador son obligatorias."));
        var sale = await repository.GetSaleForUpdateAsync(saleId, ct);
        if (sale is null || sale.InstalacionId != installation.Id)
            return Result.Failure<CancelSaleDto>(ApplicationError.NotFound("la venta"));
        if (sale.Version != request.ExpectedVersion)
            return Result.Failure<CancelSaleDto>(ApplicationError.ConcurrencyConflict("La versión de la venta cambió."));
        if (!SaleCancellationEligibility.IsCurrentConfirmedSale(sale.Estado, sale.Version, request.ExpectedVersion) ||
            sale.TipoVenta is not ("CONTADO" or "CREDITO"))
            return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("Solo puede anularse una venta confirmada de contado o crédito."));
        if (await repository.HasConfirmedChangesAsync(sale.Id, ct))
            return Result.Failure<CancelSaleDto>(ApplicationError.Conflict(
                "Una venta con cambios confirmados no admite anulación de la venta original."));
        var reasons = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId,
            "ANULACION_VENTA", ct);
        var reason = reasons.SingleOrDefault(x => x.Id == request.ReasonId && IsApprovedSaleReason(x));
        if (reason is null) return Result.Failure<CancelSaleDto>(ApplicationError.NotFound("el motivo activo de anulación de venta"));
        if (reason.Codigo == "OTRO" && observation is null)
            return Result.Failure<CancelSaleDto>(Validation("La observación es obligatoria para OTRO."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Result.Failure<CancelSaleDto>(ApplicationError.NotFound("la terminal activa"));
        MotivoOperacion? paymentReason = null;
        if (sale.TipoVenta == "CONTADO")
        {
            var paymentReasons = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId,
                "ANULACION_PAGO", ct);
            paymentReason = paymentReasons.SingleOrDefault(x => x.Codigo == reason.Codigo &&
                IsApprovedReason(x, "ANULACION_PAGO"));
            if (paymentReason is null)
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("Falta el motivo de anulación de pago aprobado."));
        }

        var originalInventory = await repository.GetSaleInventoryMovementsAsync(saleId, ct);
        var details = sale.DetalleVenta.OrderBy(x => x.ProductoId).ToArray();
        if (details.Length == 0 || originalInventory.Count != details.Length ||
            details.Any(d => originalInventory.Count(m => m.ProductoId == d.ProductoId &&
                m.CantidadStock == -d.Cantidad) != 1))
            return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("El inventario original de la venta es inconsistente."));
        var products = await repository.GetProductsForUpdateAsync(details.Select(x => x.ProductoId).ToArray(), ct);
        if (products.Count != details.Length)
            return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("Falta un producto de la venta."));
        if (details.Any(d => d.Cantidad <= 0 || products.Single(x => x.Id == d.ProductoId).StockActual >
                int.MaxValue - d.Cantidad))
            return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("La reposición excede el límite de existencias."));
        foreach (var movement in originalInventory)
            if (await repository.HasInventoryReversalAsync(movement.Id, ct))
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("El inventario de la venta ya fue revertido."));

        MovimientoCaja? originalCash = null;
        TurnoCaja? shift = null;
        SesionOperador? operatorSession = null;
        if (sale.TipoVenta == "CREDITO")
        {
            if (sale.Credito is null || sale.PagoVenta.Count != 0 ||
                await repository.GetCreditPaymentCountAsync(sale.Credito.Id, ct) != 0 ||
                sale.Credito.SaldoPendiente != sale.Total || sale.Credito.MontoAjustado != sale.Total ||
                sale.Credito.Estado != "PENDIENTE")
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("Solo se anula un crédito intacto y sin abonos."));
        }
        else
        {
            var payments = sale.PagoVenta.Where(x => x.Estado == "APLICADO").ToArray();
            if (payments.Length == 0 || payments.Any(x => x.Estado != "APLICADO" || x.MovimientoCajaId is null) ||
                payments.Sum(x => x.ValorAplicado) != sale.Total ||
                payments.Select(x => x.MovimientoCajaId).Distinct().Count() != 1)
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("Los pagos originales son inconsistentes."));
            originalCash = await repository.GetOriginalSaleCashMovementAsync(saleId, ct);
            if (originalCash is null || originalCash.Id != payments[0].MovimientoCajaId ||
                originalCash.Valor != sale.Total || originalCash.Tipo != "INGRESO" ||
                await repository.HasCashReversalAsync(originalCash.Id, ct))
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("El movimiento original de Caja no admite reversión."));
            shift = await repository.GetOpenShiftForUpdateAsync(ct);
            if (shift is null || shift.TerminalId != terminal.Id)
                return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("Se requiere un turno de Caja abierto para la devolución."));
            if (shift.ModoOperacion == CashConstants.IndividualMode)
            {
                if (shift.UsuarioResponsableId != actor.UserId)
                    return Result.Failure<CancelSaleDto>(ApplicationError.Forbidden("El turno individual pertenece a otro responsable."));
            }
            else if (shift.ModoOperacion == CashConstants.SharedMode)
            {
                operatorSession = await repository.GetActiveOperatorSessionForUpdateAsync(shift.Id, terminal.Id, actor.UserId, ct);
                if (operatorSession is null)
                    return Result.Failure<CancelSaleDto>(SharedShift.CardRequired());
            }
            else return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("La modalidad del turno no es válida."));
            var expected = await cashRepository.GetExpectedAmountsAsync(shift.Id, ct);
            var cashRefund = CashMoney.Sum(payments.Where(x => x.MetodoPago.AfectaEfectivo)
                .Select(x => x.ValorAplicado));
            var cashAvailable = CashMoney.Sum(expected.Where(x => x.AffectsCash)
                .Select(x => x.ExpectedAmount));
            if (cashRefund > cashAvailable)
                return Result.Failure<CancelSaleDto>(CashShortage.Error("anular esta venta", cashRefund, cashAvailable, false));
        }

        Usuario? authorizer;
        if (administratorSelfAuthorized)
        {
            authorizer = await repository.GetUserForUpdateAsync(actor.UserId, ct);
            if (authorizer is null || !authorizer.IsActive)
                return Result.Failure<CancelSaleDto>(ApplicationError.Forbidden());
        }
        else
        {
            var candidate = await userRepository.GetByUserNameAsync(request.Authorization!.UserName!.Trim(), ct);
            if (candidate is null) return Result.Failure<CancelSaleDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            authorizer = await repository.GetUserForUpdateAsync(candidate.Id, ct);
            if (authorizer is null || !authorizer.IsActive || authorizer.Id == actor.UserId ||
                !await CanVoidAsync(authorizer.Id, ct) ||
                !RoleHierarchy.IsHigherThan(authorizer.HighestRoleLevel, actorSnapshot!.HighestRoleLevel))
                return Result.Failure<CancelSaleDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            if (!request.Authorization.UsesCredential() && !passwordHasher.Verify(request.Authorization.Password!, authorizer.PasswordHash))
            {
                var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
                await userRepository.UpdateAsync(authorizer, ct);
                if (locked)
                    await AuditWriter.WriteAsync(auditRepository, clock, authorizer.Id, AuditAction.UserLocked,
                        nameof(Usuario), authorizer.Id, new { authorizer.Id, authorizer.FailedLoginAttempts }, ct);
                await AuditWriter.WriteCashFailureAsync(auditRepository, clock, actor.UserId,
                    AuditAction.SaleCancellationAuthorizationRejected, nameof(Venta), saleId,
                    new { SaleId = saleId, Result = "INVALID_AUTHORIZATION" }, Guid.CreateVersion7(),
                    terminal.Id, null, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Failure<CancelSaleDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            }
        }
        var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.SalesVoid, ct);
        if (permission is null || !permission.Activo)
            return Result.Failure<CancelSaleDto>(ApplicationError.Conflict("El permiso de anulación no está disponible."));
        var now = clock.UtcNow.UtcDateTime;
        var correlation = Guid.CreateVersion7();
        if (!administratorSelfAuthorized)
        {
            authorizer.RegisterSuccessfulLogin(clock.UtcNow);
            await userRepository.UpdateAsync(authorizer, ct);
        }
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.Id, PermisoId = permission.Id,
            TipoOperacion = "ANULACION_VENTA", CorrelacionId = correlation,
            DescripcionOperacion = administratorSelfAuthorized
                ? "Autorización de anulación de venta por Administrador sin segundo autorizador."
                : "Autorización de anulación de venta.", Motivo = reason.Codigo,
            MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
            CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId,
            Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = now, FechaExpiracion = now.AddMinutes(5), FechaUtilizacion = now
        };
        await repository.AddAuthorizationAsync(authorization, ct);
        var cancellation = new AnulacionVenta
        {
            Id = Guid.CreateVersion7(), VentaId = sale.Id, UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.Id, AutorizacionOperacionId = authorization.Id,
            MotivoOperacionId = reason.Id, FechaHora = now, Observacion = observation,
            CorrelacionId = correlation
        };
        await repository.AddSaleCancellationAsync(cancellation, ct);
        sale.Estado = "ANULADA";
        if (sale.Credito is not null)
        {
            foreach (var installment in sale.Credito.CuotaCredito)
            {
                installment.Monto = 0;
                installment.SaldoPendiente = 0;
                installment.FechaPago = null;
            }
            sale.Credito.Estado = "ANULADO";
            sale.Credito.MontoAjustado = 0;
            sale.Credito.SaldoPendiente = 0;
        }
        foreach (var detail in details)
        {
            var product = products.Single(x => x.Id == detail.ProductoId);
            var original = originalInventory.Single(x => x.ProductoId == detail.ProductoId);
            var stockBefore = product.StockActual;
            product.StockActual = checked(stockBefore + detail.Cantidad);
            await repository.AddInventoryMovementsAsync([new MovimientoInventario
            {
                Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actor.UserId,
                AutorizacionOperacionId = authorization.Id, AnulacionVentaId = cancellation.Id,
                TipoMovimiento = "ANULACION_VENTA", CantidadStock = detail.Cantidad,
                CantidadReservada = 0, StockAnterior = stockBefore, StockPosterior = product.StockActual,
                ReservadoAnterior = product.StockReservado, ReservadoPosterior = product.StockReservado,
                CostoPromedioAnterior = product.CostoPromedio, CostoPromedioPosterior = product.CostoPromedio,
                FechaHora = now, CorrelacionId = correlation
            }], ct);
        }
        if (originalCash is not null)
        {
            foreach (var payment in sale.PagoVenta.Where(x => x.Estado == "APLICADO"))
            {
                payment.Estado = "ANULADO";
                payment.UsuarioAnulacionId = actor.UserId;
                payment.AutorizacionAnulacionId = authorization.Id;
                payment.MotivoAnulacionId = paymentReason!.Id;
                payment.FechaAnulacion = now;
                payment.ObservacionAnulacion = observation;
            }
            await repository.AddCashMovementAsync(new MovimientoCaja
            {
                Id = Guid.CreateVersion7(), TurnoCajaId = shift!.Id, UsuarioId = actor.UserId,
                SesionOperadorId = operatorSession?.Id,
                MovimientoRevertidoId = originalCash.Id, Tipo = "REVERSO",
                CategoriaMovimiento = "REVERSO_PAGO", Valor = sale.Total,
                FechaHora = now, CorrelacionId = correlation
            }, ct);
        }
        var response = new CancelSaleDto(sale.Id, VisibleNumber(sale), sale.Estado,
            checked(request.ExpectedVersion + 1), cancellation.Id, correlation,
            new DateTimeOffset(now, TimeSpan.Zero), sale.TipoVenta,
            originalCash is null ? 0m : sale.Total);
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = operatorSession?.Id,
            ClaveIdempotencia = key, TipoOperacion = "ANULAR_VENTA",
            HashSolicitud = fingerprint, Entidad = nameof(Venta), EntidadId = sale.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation,
            FechaRegistro = now
        }, ct);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId,
            AuditAction.SaleCancelled, nameof(Venta), sale.Id,
            new { SaleId = sale.Id, From = "CONFIRMADA", To = "ANULADA", ReasonId = reason.Id,
                AuthorizationId = authorization.Id,
                AuthorizationMode = administratorSelfAuthorized ? "ADMINISTRATOR_SELF" : "OTHER_USER",
                SaleType = sale.TipoVenta, ReversedAmount = response.ReversedAmount,
                CorrelationId = correlation }, correlation, authorizer.Id, operatorSession?.Id, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        if (operatorSession is not null)
        {
            operatorSession.Close(now, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId,
                AuditAction.OperatorSessionClosed, nameof(SesionOperador), operatorSession.Id,
                new { operatorSession.Estado, SaleId = sale.Id, SessionConsumedBySaleCancellation = true },
                correlation, null, operatorSession.Id, terminal.Id, null, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success(response);
    }

    private async Task<bool> CanVoidAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(userId, ct);
        return snapshot is not null && SaleCancellationEligibility.HasAuthorizedRole(snapshot.HighestRoleLevel) &&
            snapshot.PermissionCodes.Contains(PermissionCodes.SalesVoid, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsApprovedSaleReason(MotivoOperacion reason)
    {
        return IsApprovedReason(reason, "ANULACION_VENTA");
    }

    private static bool IsApprovedReason(MotivoOperacion reason, string operationType)
    {
        var index = Array.FindIndex(SaleCancellationCatalog, x => x.Code == reason.Codigo);
        return index >= 0 && reason.Activo && reason.TipoOperacion == operationType &&
            reason.Nombre == SaleCancellationCatalog[index].Name &&
            reason.Descripcion == SaleCancellationCatalog[index].Name &&
            reason.OrdenVisual == (index + 1) * 10;
    }

    private static SaleCancellationReasonDto ToCancellationReason(MotivoOperacion reason) =>
        new(reason.Id, reason.Codigo, reason.Nombre, reason.OrdenVisual);
}
