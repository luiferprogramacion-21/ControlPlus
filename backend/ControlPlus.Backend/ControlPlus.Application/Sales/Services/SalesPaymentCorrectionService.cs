using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

/// <summary>
/// Corrects the payment methods of a confirmed cash sale (approved 19 November 2026: «corregir los medios de pago mediante
/// una operación trazable que conserve la venta y el total cobrado»). Example: it was paid with Nequi but recorded as cash.
/// The sale, its products and its total never change. The original payments are annulled with reason and authorization,
/// their cash movement is reversed and a new movement carries the correct payments, the same reversal model the shift
/// already understands, so the expected amount per method stays consistent. Decision D3 (23 November 2026): a sale of an
/// already closed shift is corrected too; that closure is never rewritten, the reversal and the new payments live in
/// today's open shift (migration 20261124020000_ClosedShiftPaymentCorrectionV1) and keep the sale's correlation.
/// </summary>
public sealed partial class SalesService
{
    private const string PaymentCorrectionOperation = "CORREGIR_PAGO_VENTA";

    public async Task<Result<CorrectSalePaymentsDto>> CorrectPaymentsAsync(ActorContext actor, Guid saleId,
        string idempotencyKey, CorrectSalePaymentsRequest request, CancellationToken cancellationToken = default)
    {
        if (!await CanVoidAsync(actor.UserId, cancellationToken))
            return Result.Failure<CorrectSalePaymentsDto>(ApplicationError.Forbidden());
        var key = idempotencyKey?.Trim() ?? "";
        if (saleId == Guid.Empty || request is null || request.ReasonId == Guid.Empty ||
            request.ExpectedVersion <= 0 || key.Length is < 1 or > 100 ||
            key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')) ||
            request.Observation?.Length > 500 || request.Payments is null || request.Payments.Count is < 1 or > 3)
            return Result.Failure<CorrectSalePaymentsDto>(Validation("Elige el motivo y de uno a tres medios de pago."));
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<CorrectSalePaymentsDto>(ApplicationError.ConcurrencyConflict("La versión de la venta está agotada."));
        var payments = request.Payments.Select(x => new NormalizedPayment(NormalizeCode(x.MethodCode), x.AppliedAmount,
            x.ReceivedAmount, NormalizeOptional(x.Reference))).ToArray();
        if (payments.Any(x => !ApprovedPaymentMethods.Contains(x.MethodCode)))
            return Result.Failure<CorrectSalePaymentsDto>(Validation("Solo se admiten efectivo, transferencia y Nequi."));
        if (payments.Select(x => x.MethodCode).Distinct(StringComparer.Ordinal).Count() != payments.Length)
            return Result.Failure<CorrectSalePaymentsDto>(Validation("No repitas un medio de pago."));
        var scanned = await ResolveScannedAuthorizationAsync(request.Authorization, cancellationToken);
        if (scanned.Error is not null) return Result.Failure<CorrectSalePaymentsDto>(scanned.Error);
        request = request with { Authorization = scanned.Authorization };
        var observation = string.IsNullOrWhiteSpace(request.Observation) ? null : request.Observation.Trim();
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            SaleId = saleId, request.ExpectedVersion, request.ReasonId, Observation = observation, Actor = actor.UserId,
            Payments = payments.OrderBy(x => x.MethodCode, StringComparer.Ordinal)
        }));
        var account = string.IsNullOrWhiteSpace(request.Authorization?.UserName)
            ? $"SALE-PAYMENT-CORRECTION:{actor.UserId:N}"
            : request.Authorization.UserName.Trim().ToUpperInvariant();
        return await loginAttemptCoordinator.ExecuteAsync(account,
            token => CorrectPaymentsWithinTransactionAsync(actor, saleId, key, request, payments, observation, fingerprint, token),
            cancellationToken);
    }

    private async Task<Result<CorrectSalePaymentsDto>> CorrectPaymentsWithinTransactionAsync(ActorContext actor,
        Guid saleId, string key, CorrectSalePaymentsRequest request, NormalizedPayment[] newPayments, string? observation,
        string fingerprint, CancellationToken ct)
    {
        static Result<CorrectSalePaymentsDto> Fail(ApplicationError error) => Result.Failure<CorrectSalePaymentsDto>(error);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Fail(ApplicationError.NotFound("la instalación activa"));
        if (!await CanVoidAsync(actor.UserId, ct)) return Fail(ApplicationError.Forbidden());
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != PaymentCorrectionOperation ||
                previous.HashSolicitud != fingerprint || previous.EntidadId != saleId || previous.Resultado is null)
                return Fail(ApplicationError.Conflict("Esta corrección ya se envió con otros datos. Actualiza la pantalla."));
            CorrectSalePaymentsDto? saved;
            try { saved = JsonSerializer.Deserialize<CorrectSalePaymentsDto>(previous.Resultado); }
            catch (JsonException) { saved = null; }
            return saved is null ? Fail(ApplicationError.Conflict("La corrección original no es recuperable.")) : Result.Success(saved);
        }
        var actorSnapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        var administratorSelfAuthorized = actorSnapshot?.HighestRoleLevel == RoleLevel.Administrador &&
            request.Authorization is null;
        if (!administratorSelfAuthorized && (request.Authorization is null || !request.Authorization.IsComplete()))
            return Fail(Validation("Un Administrador debe autorizar la corrección."));

        var sale = await repository.GetSaleForUpdateAsync(saleId, ct);
        if (sale is null || sale.InstalacionId != installation.Id) return Fail(ApplicationError.NotFound("la venta"));
        if (sale.Version != request.ExpectedVersion)
            return Fail(ApplicationError.ConcurrencyConflict("La venta cambió. Actualízala antes de corregir."));
        if (sale.Estado != "CONFIRMADA" || sale.TipoVenta != "CONTADO")
            return Fail(ApplicationError.Conflict("Solo se corrige el medio de pago de una venta de contado confirmada."));
        var current = sale.PagoVenta.Where(x => x.Estado == "APLICADO").ToArray();
        if (current.Length == 0 || current.Any(x => x.MovimientoCajaId is null) ||
            current.Select(x => x.MovimientoCajaId).Distinct().Count() != 1 || current.Sum(x => x.ValorAplicado) != sale.Total)
            return Fail(ApplicationError.Conflict("Los pagos de la venta no admiten una corrección segura."));

        var methods = await repository.GetPaymentMethodsAsync(newPayments.Select(x => x.MethodCode).ToArray(), ct);
        var methodsByCode = methods.ToDictionary(x => x.Codigo, StringComparer.Ordinal);
        if (methods.Count != newPayments.Length || methods.Any(x => !x.Activo))
            return Fail(Validation("Todos los medios de pago deben existir y estar activos."));
        ApplicationError? paymentError;
        try { paymentError = ValidatePayments(newPayments, methodsByCode, sale.Total); }
        catch (CashMoneyException) { paymentError = Validation("Los valores deben ser pesos enteros."); }
        if (paymentError is not null) return Fail(paymentError);
        static string Signature(IEnumerable<(string Code, decimal Amount)> rows) =>
            string.Join("|", rows.OrderBy(x => x.Code, StringComparer.Ordinal).Select(x => $"{x.Code}:{x.Amount}"));
        if (Signature(current.Select(x => (x.MetodoPago.Codigo, x.ValorAplicado))) ==
            Signature(newPayments.Select(x => (x.MethodCode, x.AppliedAmount))))
            return Fail(Validation("Los medios y valores son iguales a los de la venta: no hay nada que corregir."));

        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Fail(ApplicationError.NotFound("la terminal activa"));
        var shift = await repository.GetOpenShiftForUpdateAsync(ct);
        if (shift is null || shift.TerminalId != terminal.Id)
            return Fail(ApplicationError.Conflict("Abre la caja para corregir el medio de pago."));
        // Decisión D3 del 23/11/2026: una venta de un turno ya cerrado también se corrige. Ese cierre no cambia: el reverso y
        // el pago correcto se registran en la caja abierta y quedan unidos a la venta (y a su día) por la correlación.
        var fromClosedShift = sale.TurnoCajaId != shift.Id;
        SesionOperador? operatorSession = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (shift.UsuarioResponsableId != actor.UserId)
                return Fail(ApplicationError.Forbidden("La caja está abierta a nombre de otra persona: solo ella corrige sus ventas."));
        }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            operatorSession = await repository.GetActiveOperatorSessionForUpdateAsync(shift.Id, terminal.Id, actor.UserId, ct);
            if (operatorSession is null) return Fail(ApplicationError.Conflict("Pasa tu carné en Caja antes de corregir."));
        }
        else return Fail(ApplicationError.Conflict("La modalidad del turno no es válida."));

        var originalMovement = await repository.GetOriginalSaleCashMovementAsync(sale.Id, ct);
        if (originalMovement is null || originalMovement.Id != current[0].MovimientoCajaId ||
            originalMovement.Tipo != "INGRESO" || originalMovement.Valor != sale.Total ||
            await repository.HasCashReversalAsync(originalMovement.Id, ct))
            return Fail(ApplicationError.Conflict("El movimiento de caja de la venta no admite la corrección."));

        // Moving money from cash to an electronic method lowers the cash expected in the drawer; it cannot go below zero.
        var cashBefore = CashMoney.Sum(current.Where(x => x.MetodoPago.AfectaEfectivo).Select(x => x.ValorAplicado));
        var cashAfter = CashMoney.Sum(newPayments.Where(x => methodsByCode[x.MethodCode].AfectaEfectivo).Select(x => x.AppliedAmount));
        if (cashAfter < cashBefore)
        {
            var expected = await cashRepository.GetExpectedAmountsAsync(shift.Id, ct);
            var cashAvailable = CashMoney.Sum(expected.Where(x => x.AffectsCash).Select(x => x.ExpectedAmount));
            if (CashMoney.Subtract(cashBefore, cashAfter) > cashAvailable)
                return Fail(ApplicationError.Conflict("El efectivo esperado de la caja no alcanza para esta corrección."));
        }

        var saleReasons = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId, "ANULACION_VENTA", ct);
        var chosen = saleReasons.SingleOrDefault(x => x.Id == request.ReasonId && IsApprovedSaleReason(x));
        if (chosen is null) return Fail(ApplicationError.NotFound("el motivo"));
        if (chosen.Codigo == "OTRO" && observation is null) return Fail(Validation("Escribe una observación para el motivo Otro."));
        var paymentReasons = await repository.GetCancellationReasonsAsync(installation.EstablecimientoId, "ANULACION_PAGO", ct);
        var paymentReason = paymentReasons.SingleOrDefault(x => x.Codigo == chosen.Codigo && IsApprovedReason(x, "ANULACION_PAGO"));
        if (paymentReason is null) return Fail(ApplicationError.Conflict("Falta el motivo de anulación de pago aprobado."));

        Usuario? authorizer;
        if (administratorSelfAuthorized)
        {
            authorizer = await repository.GetUserForUpdateAsync(actor.UserId, ct);
            if (authorizer is null || !authorizer.IsActive) return Fail(ApplicationError.Forbidden());
        }
        else
        {
            var invalid = ApplicationError.Unauthorized("Usuario o contraseña del autorizador incorrectos.");
            var candidate = await userRepository.GetByUserNameAsync(request.Authorization!.UserName!.Trim(), ct);
            if (candidate is null) return Fail(invalid);
            authorizer = await repository.GetUserForUpdateAsync(candidate.Id, ct);
            var authorizerSnapshot = authorizer is null ? null : await userRepository.GetAuthorizationSnapshotAsync(authorizer.Id, ct);
            if (authorizer is null || authorizerSnapshot is null || !authorizer.IsActive || authorizer.Id == actor.UserId ||
                authorizerSnapshot.HighestRoleLevel != RoleLevel.Administrador || !await CanVoidAsync(authorizer.Id, ct))
                return Fail(invalid);
            if (!request.Authorization.UsesCredential() && !passwordHasher.Verify(request.Authorization.Password!, authorizer.PasswordHash))
            {
                var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
                await userRepository.UpdateAsync(authorizer, ct);
                if (locked)
                    await AuditWriter.WriteAsync(auditRepository, clock, authorizer.Id, AuditAction.UserLocked,
                        nameof(Usuario), authorizer.Id, new { authorizer.Id, authorizer.FailedLoginAttempts }, ct);
                await AuditWriter.WriteCashFailureAsync(auditRepository, clock, actor.UserId,
                    AuditAction.SalePaymentCorrectionAuthorizationRejected, nameof(Venta), saleId,
                    new { SaleId = saleId, Result = "INVALID_AUTHORIZATION" }, Guid.CreateVersion7(), terminal.Id, null, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Fail(invalid);
            }
            authorizer.RegisterSuccessfulLogin(clock.UtcNow);
            await userRepository.UpdateAsync(authorizer, ct);
        }
        var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.SalesVoid, ct);
        if (permission is null || !permission.Activo) return Fail(ApplicationError.Conflict("El permiso para corregir ventas no está disponible."));

        var now = clock.UtcNow;
        var correlation = Guid.CreateVersion7();
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId, UsuarioAutorizadorId = authorizer.Id,
            PermisoId = permission.Id, TipoOperacion = "CORRECCION_MEDIO_PAGO", CorrelacionId = correlation,
            DescripcionOperacion = administratorSelfAuthorized
                ? "Corrección de medio de pago por Administrador sin segundo autorizador."
                : "Autorización de corrección de medio de pago.",
            Motivo = chosen.Codigo, MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
            CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId,
            Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime, FechaUtilizacion = now.UtcDateTime
        };
        await repository.AddAuthorizationAsync(authorization, ct);
        foreach (var payment in current)
        {
            payment.Estado = "ANULADO";
            payment.UsuarioAnulacionId = actor.UserId;
            payment.AutorizacionAnulacionId = authorization.Id;
            payment.MotivoAnulacionId = paymentReason.Id;
            payment.FechaAnulacion = now.UtcDateTime;
            payment.ObservacionAnulacion = observation;
        }
        await repository.AddCashMovementAsync(new MovimientoCaja
        {
            Id = Guid.CreateVersion7(), TurnoCajaId = shift.Id, UsuarioId = actor.UserId, SesionOperadorId = operatorSession?.Id,
            MovimientoRevertidoId = originalMovement.Id, Tipo = "REVERSO", CategoriaMovimiento = "REVERSO_PAGO",
            Valor = sale.Total, Concepto = fromClosedShift
                ? $"Corrección de medio de pago de {VisibleNumber(sale)} (venta del {sale.FechaOperativa:dd/MM/yyyy}, caja ya cerrada)"
                : $"Corrección de medio de pago de {VisibleNumber(sale)}",
            FechaHora = now.UtcDateTime, CorrelacionId = correlation
        }, ct);
        // The database accepts the new payment of a closed shift's sale only next to this reversal (D3): it is saved first,
        // inside the same transaction, so the order of the inserts never matters.
        if (fromClosedShift) await unitOfWork.SaveChangesAsync(ct);
        var corrected = MovimientoCaja.Create(shift.Id, actor.UserId, operatorSession?.Id, CashConstants.IncomeMovement,
            "VENTA", sale.Total, $"Venta {VisibleNumber(sale)} (medio corregido)", correlation, now);
        await repository.AddCashMovementAsync(corrected, ct);
        var rows = newPayments.Select(payment => new PagoVenta
        {
            Id = Guid.CreateVersion7(), VentaId = sale.Id, MetodoPagoId = methodsByCode[payment.MethodCode].Id,
            MovimientoCajaId = corrected.Id, UsuarioId = actor.UserId, SesionOperadorId = operatorSession?.Id,
            ValorAplicado = payment.AppliedAmount, ValorRecibido = payment.ReceivedAmount,
            Cambio = payment.ReceivedAmount is null ? 0m : payment.ReceivedAmount.Value - payment.AppliedAmount,
            Referencia = payment.Reference, FechaHora = now.UtcDateTime, Estado = "APLICADO"
        }).ToArray();
        await repository.AddPaymentsAsync(rows, ct);
        sale.Version = checked(sale.Version + 1);

        var response = new CorrectSalePaymentsDto(sale.Id, VisibleNumber(sale), sale.Version, correlation,
            rows.OrderBy(x => methodsByCode.Values.Single(m => m.Id == x.MetodoPagoId).OrdenVisual).Select(x =>
            {
                var method = methodsByCode.Values.Single(m => m.Id == x.MetodoPagoId);
                return new SalePaymentDto(x.Id, method.Codigo, method.Nombre, x.ValorAplicado, x.ValorRecibido, x.Cambio, x.Referencia);
            }).ToArray(),
            CashMoney.Subtract(cashAfter, cashBefore));
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id, UsuarioId = actor.UserId,
            SesionOperadorId = operatorSession?.Id, ClaveIdempotencia = key, TipoOperacion = PaymentCorrectionOperation,
            HashSolicitud = fingerprint, Entidad = nameof(Venta), EntidadId = sale.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation, FechaRegistro = now.UtcDateTime
        }, ct);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.SalePaymentsCorrected,
            nameof(Venta), sale.Id, new
            {
                Sale = VisibleNumber(sale), Total = sale.Total,
                Before = current.Select(x => new { Method = x.MetodoPago.Nombre, Amount = x.ValorAplicado }).ToArray(),
                After = response.Payments.Select(x => new { Method = x.MethodName, Amount = x.AppliedAmount }).ToArray(),
                CashDifference = response.CashDifference, AuthorizationId = authorization.Id,
                FromClosedShift = fromClosedShift, OriginalShiftId = sale.TurnoCajaId, OriginalOperatingDate = sale.FechaOperativa,
                AuthorizationMode = administratorSelfAuthorized ? "ADMINISTRATOR_SELF" : "OTHER_USER"
            }, correlation, authorizer.Id, operatorSession?.Id, terminal.Id, chosen.Codigo, ct);
        await unitOfWork.SaveChangesAsync(ct);
        if (operatorSession is not null)
        {
            // Like a sale or a cancellation, the correction uses up the shared-shift session.
            operatorSession.Close(now.UtcDateTime, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorSessionClosed,
                nameof(SesionOperador), operatorSession.Id,
                new { operatorSession.Estado, SaleId = sale.Id, SessionConsumedByPaymentCorrection = true },
                correlation, null, operatorSession.Id, terminal.Id, null, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success(response);
    }
}
