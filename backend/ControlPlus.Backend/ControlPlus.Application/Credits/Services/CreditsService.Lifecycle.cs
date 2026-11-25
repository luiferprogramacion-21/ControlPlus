using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.Credits;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Credits.Services;

public sealed partial class CreditsService
{
    private static readonly (string Code, string Name)[] PaymentCancellationCatalog =
        ApprovedReasonCatalogs.SaleCancellation;

    public async Task<Result<IReadOnlyCollection<CreditPaymentCancellationReasonDto>>>
        ListPaymentCancellationReasonsAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        if (await GetLifecycleActorAsync(actor, RoleLevel.Supervisor, cancellationToken) is null)
            return Result.Failure<IReadOnlyCollection<CreditPaymentCancellationReasonDto>>(
                ApplicationError.Forbidden());
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<IReadOnlyCollection<CreditPaymentCancellationReasonDto>>(
                ApplicationError.NotFound("la instalación activa"));
        var reasons = await sales.GetCancellationReasonsAsync(installation.EstablecimientoId,
            ApprovedReasonCatalogs.SalePaymentCancellationType, cancellationToken);
        return Result.Success<IReadOnlyCollection<CreditPaymentCancellationReasonDto>>(reasons
            .Where(IsApprovedPaymentCancellationReason).OrderBy(x => x.OrdenVisual)
            .Select(x => new CreditPaymentCancellationReasonDto(x.Id, x.Codigo, x.Nombre, x.OrdenVisual))
            .ToArray());
    }

    public async Task<Result<CancelCreditPaymentDto>> CancelPaymentAsync(ActorContext actor, Guid creditId,
        Guid paymentId, string idempotencyKey, CancelCreditPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await GetLifecycleActorAsync(actor, RoleLevel.Supervisor, cancellationToken) is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Forbidden());
        var validation = ValidateCancellation(creditId, paymentId, idempotencyKey, request);
        if (validation is not null) return Result.Failure<CancelCreditPaymentDto>(validation);
        var resolved = await ResolveAuthorizationAsync(request.Authorization, cancellationToken);
        if (resolved.Error is not null) return Result.Failure<CancelCreditPaymentDto>(resolved.Error);
        request = request with { Authorization = resolved.Authorization };
        var observation = string.IsNullOrWhiteSpace(request.Observation) ? null : request.Observation.Trim();
        var key = idempotencyKey.Trim();
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            CreditId = creditId, PaymentId = paymentId, request.ExpectedVersion,
            request.ReasonId, Observation = observation, Actor = actor.UserId
        }));
        var account = string.IsNullOrWhiteSpace(request.Authorization?.UserName)
            ? $"CREDIT-PAYMENT-CANCEL-REPLAY:{actor.UserId:N}"
            : request.Authorization.UserName.Trim().ToUpperInvariant();
        return await loginAttempts.ExecuteAsync(account, token => CancelPaymentWithinTransactionAsync(
            actor, creditId, paymentId, key, request, observation, fingerprint, token), cancellationToken);
    }

    private async Task<Result<CancelCreditPaymentDto>> CancelPaymentWithinTransactionAsync(ActorContext actor,
        Guid creditId, Guid paymentId, string key, CancelCreditPaymentRequest request, string? observation,
        string fingerprint, CancellationToken cancellationToken)
    {
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.NotFound("la instalación activa"));
        var actorSnapshot = await GetLifecycleActorAsync(actor, RoleLevel.Supervisor, cancellationToken);
        if (actorSnapshot is null) return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Forbidden());
        var previous = await sales.GetIdempotencyAsync(installation.Id, key, cancellationToken);
        if (previous is not null)
            return Replay<CancelCreditPaymentDto>(previous, actor.UserId, "ANULAR_ABONO_CREDITO",
                creditId, fingerprint, "La anulación original no es recuperable.");

        var terminal = await sales.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.NotFound("la terminal activa"));
        var administratorSelf = actorSnapshot.HighestRoleLevel == RoleLevel.Administrador &&
            request.Authorization is null;
        if (actorSnapshot.HighestRoleLevel == RoleLevel.Administrador && request.Authorization is not null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Validation(
                "El Administrador anula el abono sin un segundo autorizador."));
        if (!administratorSelf && (request.Authorization is null || !request.Authorization.IsComplete()))
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Validation(
                "La autorización de un Administrador es obligatoria."));

        var credit = await credits.GetForUpdateAsync(creditId, cancellationToken);
        if (credit is null) return Result.Failure<CancelCreditPaymentDto>(ApplicationError.NotFound("el crédito"));
        if (credit.Version != request.ExpectedVersion)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.ConcurrencyConflict(
                "El crédito fue modificado. Recárguelo e intente nuevamente."));
        if (credit.Estado == "ANULADO")
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "Un crédito anulado no admite anulaciones de abonos."));
        var selectedPayment = await credits.GetPaymentForUpdateAsync(creditId, paymentId, cancellationToken);
        if (selectedPayment is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.NotFound("el abono"));
        if (selectedPayment.Estado != "APLICADO" || selectedPayment.MovimientoCajaId is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "El abono ya fue anulado o no tiene un movimiento de Caja reversible."));
        var originalMovement = await credits.GetCashMovementForUpdateAsync(
            selectedPayment.MovimientoCajaId.Value, cancellationToken);
        var paymentRows = (await credits.GetPaymentsForCashMovementForUpdateAsync(
            selectedPayment.MovimientoCajaId.Value, cancellationToken)).ToArray();
        decimal reversedAmount;
        try { reversedAmount = CashMoney.Sum(paymentRows.Select(x => x.ValorAplicado)); }
        catch (Exception exception) when (exception is CashMoneyException or OverflowException)
        {
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "Los importes del abono no permiten una reversión segura."));
        }
        if (originalMovement is null || originalMovement.Tipo != CashConstants.IncomeMovement ||
            originalMovement.CategoriaMovimiento != "ABONO_CREDITO" ||
            originalMovement.Valor != reversedAmount || paymentRows.Length == 0 ||
            paymentRows.Any(x => x.CreditoId != creditId || x.Estado != "APLICADO") ||
            await credits.HasCashReversalAsync(originalMovement.Id, cancellationToken))
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "El abono combinado o su movimiento de Caja no admiten reversión."));
        var installments = (await credits.GetInstallmentsForUpdateAsync(credit.Id, cancellationToken)).ToArray();
        var applications = (await credits.GetPaymentApplicationsForUpdateAsync(
            paymentRows.Select(x => x.Id).ToArray(), cancellationToken)).ToArray();
        if (installments.Length == 0 || applications.Sum(x => x.ValorAplicado) != reversedAmount)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "Las aplicaciones del abono a cuotas no permiten una reversión segura."));

        if (credit.SaldoPendiente > credit.MontoAjustado - reversedAmount)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "El saldo del crédito es inconsistente con el abono."));

        var reasons = await sales.GetCancellationReasonsAsync(installation.EstablecimientoId,
            ApprovedReasonCatalogs.SalePaymentCancellationType, cancellationToken);
        var reason = reasons.SingleOrDefault(x => x.Id == request.ReasonId &&
            IsApprovedPaymentCancellationReason(x));
        if (reason is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.NotFound(
                "el motivo activo de anulación de pago"));
        if (reason.Codigo == "OTRO" && observation is null)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Validation(
                "La observación es obligatoria para OTRO."));

        var shift = await sales.GetOpenShiftForUpdateAsync(cancellationToken);
        if (shift is null || shift.TerminalId != terminal.Id)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "Se requiere el turno actual de Caja abierto para revertir el abono."));
        SesionOperador? operatorSession = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (shift.UsuarioResponsableId != actor.UserId)
                return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Forbidden(
                    "El turno individual pertenece a otro responsable."));
        }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            operatorSession = await sales.GetActiveOperatorSessionForUpdateAsync(
                shift.Id, terminal.Id, actor.UserId, cancellationToken);
            if (operatorSession is null)
                return Result.Failure<CancelCreditPaymentDto>(SharedShift.CardRequired());
        }
        else return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
            "La modalidad del turno no es válida."));

        var methods = await credits.GetPaymentMethodsByIdAsync(
            paymentRows.Select(x => x.MetodoPagoId).ToArray(), cancellationToken);
        if (methods.Count != paymentRows.Select(x => x.MetodoPagoId).Distinct().Count())
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "Falta un medio de pago del abono."));
        var cashRefund = CashMoney.Sum(paymentRows.Join(methods.Where(x => x.AfectaEfectivo),
            payment => payment.MetodoPagoId, method => method.Id, (payment, _) => payment.ValorAplicado));
        var expected = await cashRepository.GetExpectedAmountsAsync(shift.Id, cancellationToken);
        var cashAvailable = CashMoney.Sum(expected.Where(x => x.AffectsCash).Select(x => x.ExpectedAmount));
        if (cashRefund > cashAvailable)
            return Result.Failure<CancelCreditPaymentDto>(CashShortage.Error("anular este abono", cashRefund, cashAvailable, false));

        var authorizer = await ResolveCancellationAuthorizerAsync(actor, actorSnapshot, request.Authorization,
            administratorSelf, terminal.Id, creditId, cancellationToken);
        if (authorizer.Error is not null) return Result.Failure<CancelCreditPaymentDto>(authorizer.Error);
        var permission = await permissions.GetByCodeAsync(PermissionCodes.CreditManage, cancellationToken);
        if (permission is null || !permission.Activo)
            return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                "El permiso de gestión de créditos no está disponible."));
        var now = clock.UtcNow;
        var today = await TodayAsync(cancellationToken);
        var correlation = Guid.CreateVersion7();
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.User!.Id,
            CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId,
            PermisoId = permission.Id, TipoOperacion = "ANULACION_PAGO_CREDITO",
            CorrelacionId = correlation,
            DescripcionOperacion = administratorSelf
                ? "Autorización de anulación de abono por Administrador sin segundo autorizador."
                : "Autorización de anulación de abono de crédito por Administrador.",
            Motivo = reason.Codigo, MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
            Resultado = "EXITOSA", Estado = "UTILIZADA", FechaSolicitud = now.UtcDateTime,
            FechaExpiracion = now.UtcDateTime.AddMinutes(5), FechaUtilizacion = now.UtcDateTime
        };
        await sales.AddAuthorizationAsync(authorization, cancellationToken);
        foreach (var payment in paymentRows)
        {
            payment.Estado = "ANULADO";
            payment.UsuarioAnulacionId = actor.UserId;
            payment.AutorizacionAnulacionId = authorization.Id;
            payment.MotivoAnulacionId = reason.Id;
            payment.FechaAnulacion = now.UtcDateTime;
            payment.ObservacionAnulacion = observation;
        }
        var installmentsById = installments.ToDictionary(x => x.Id);
        foreach (var application in applications)
        {
            if (!installmentsById.TryGetValue(application.CuotaCreditoId, out var installment) ||
                installment.SaldoPendiente + application.ValorAplicado > installment.Monto)
                return Result.Failure<CancelCreditPaymentDto>(ApplicationError.Conflict(
                    "Una cuota no admite restituir el importe del abono."));
            installment.SaldoPendiente += application.ValorAplicado;
            installment.FechaPago = null;
        }
        credit.SaldoPendiente += reversedAmount;
        credit.Estado = CreditStatusPolicy.ResolveOpenState(
            installments.Select(x => (x.FechaVencimiento, x.SaldoPendiente)), today);
        credit.FechaPago = null;
        await sales.AddCashMovementAsync(new MovimientoCaja
        {
            Id = Guid.CreateVersion7(), TurnoCajaId = shift.Id, UsuarioId = actor.UserId,
            SesionOperadorId = operatorSession?.Id, MovimientoRevertidoId = originalMovement.Id,
            Tipo = "REVERSO", CategoriaMovimiento = "REVERSO_PAGO", Valor = reversedAmount,
            Concepto = "Anulación de abono de crédito", FechaHora = now.UtcDateTime,
            CorrelacionId = correlation
        }, cancellationToken);
        var response = new CancelCreditPaymentDto(credit.Id, credit.SaldoPendiente, credit.Estado,
            checked(request.ExpectedVersion + 1), paymentRows.Select(x => x.Id).ToArray(), correlation,
            now, reversedAmount);
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = operatorSession?.Id, ClaveIdempotencia = key,
            TipoOperacion = "ANULAR_ABONO_CREDITO", HashSolicitud = fingerprint,
            Entidad = nameof(Credito), EntidadId = credit.Id, Resultado = JsonSerializer.Serialize(response),
            CorrelacionId = correlation, FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.CreditPaymentCancelled,
            nameof(PagoCredito), selectedPayment.Id,
            new { CreditId = credit.Id, PaymentIds = paymentRows.Select(x => x.Id).ToArray(),
                ReasonId = reason.Id, AuthorizationId = authorization.Id,
                AuthorizationMode = administratorSelf ? "ADMINISTRATOR_SELF" : "OTHER_USER",
                OriginalShiftId = originalMovement.TurnoCajaId, ReversalShiftId = shift.Id,
                ReversedAmount = reversedAmount, CorrelationId = correlation },
            correlation, authorizer.User.Id, operatorSession?.Id, terminal.Id, reason.Codigo,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (operatorSession is not null)
        {
            operatorSession.Close(now.UtcDateTime, "CAMBIO_OPERADOR");
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(response);
    }

    public async Task<Result<ForgiveCreditDto>> ForgiveAsync(ActorContext actor, Guid creditId,
        string idempotencyKey, ForgiveCreditRequest request, CancellationToken cancellationToken = default)
    {
        var actorSnapshot = await GetLifecycleActorAsync(actor, RoleLevel.Administrador, cancellationToken);
        if (actorSnapshot is null) return Result.Failure<ForgiveCreditDto>(ApplicationError.Forbidden());
        var key = idempotencyKey?.Trim() ?? string.Empty;
        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (creditId == Guid.Empty || request is null || request.ExpectedVersion <= 0 ||
            request.ExpectedVersion == long.MaxValue || reason.Length is < 1 or > 500 || !ValidKey(key))
            return Result.Failure<ForgiveCreditDto>(ApplicationError.Validation(
                "Idempotency-Key, versión y motivo son obligatorios."));
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            CreditId = creditId, request.ExpectedVersion, Reason = reason, Actor = actor.UserId
        }));
        await using var transaction = await transactions.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<ForgiveCreditDto>(ApplicationError.NotFound("la instalación activa"));
        actorSnapshot = await GetLifecycleActorAsync(actor, RoleLevel.Administrador, cancellationToken);
        if (actorSnapshot is null) return Result.Failure<ForgiveCreditDto>(ApplicationError.Forbidden());
        var previous = await sales.GetIdempotencyAsync(installation.Id, key, cancellationToken);
        if (previous is not null)
            return Replay<ForgiveCreditDto>(previous, actor.UserId, "CONDONAR_CREDITO", creditId,
                fingerprint, "La condonación original no es recuperable.");
        var terminal = await sales.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null)
            return Result.Failure<ForgiveCreditDto>(ApplicationError.NotFound("la terminal activa"));
        var credit = await credits.GetForUpdateAsync(creditId, cancellationToken);
        if (credit is null) return Result.Failure<ForgiveCreditDto>(ApplicationError.NotFound("el crédito"));
        if (credit.Version != request.ExpectedVersion)
            return Result.Failure<ForgiveCreditDto>(ApplicationError.ConcurrencyConflict(
                "El crédito fue modificado. Recárguelo e intente nuevamente."));
        if (credit.Estado == "ANULADO" || credit.SaldoPendiente <= 0)
            return Result.Failure<ForgiveCreditDto>(ApplicationError.Conflict(
                "Solo puede condonarse un crédito con saldo pendiente."));
        var administrator = await sales.GetUserForUpdateAsync(actor.UserId, cancellationToken);
        if (administrator is null || !administrator.IsActive)
            return Result.Failure<ForgiveCreditDto>(ApplicationError.Forbidden());
        var permission = await permissions.GetByCodeAsync(PermissionCodes.CreditManage, cancellationToken);
        if (permission is null || !permission.Activo)
            return Result.Failure<ForgiveCreditDto>(ApplicationError.Conflict(
                "El permiso de gestión de créditos no está disponible."));
        var now = clock.UtcNow;
        var correlation = Guid.CreateVersion7();
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = actor.UserId, PermisoId = permission.Id,
            TipoOperacion = "CONDONACION_CREDITO", CorrelacionId = correlation,
            DescripcionOperacion = "Condonación del saldo de crédito por Administrador.",
            Motivo = reason, MetodoAutenticacion = "PASSWORD", Resultado = "EXITOSA",
            Estado = "UTILIZADA", FechaSolicitud = now.UtcDateTime,
            FechaExpiracion = now.UtcDateTime.AddMinutes(5), FechaUtilizacion = now.UtcDateTime
        };
        await sales.AddAuthorizationAsync(authorization, cancellationToken);
        var forgivenAmount = credit.SaldoPendiente;
        var previousState = credit.Estado;
        var installments = await credits.GetInstallmentsForUpdateAsync(credit.Id, cancellationToken);
        foreach (var installment in installments)
        {
            installment.SaldoPendiente = 0;
            installment.FechaPago = null;
        }
        credit.SaldoPendiente = 0;
        credit.Estado = "ANULADO";
        credit.FechaPago = null;
        var response = new ForgiveCreditDto(credit.Id, 0, credit.Estado,
            checked(request.ExpectedVersion + 1), authorization.Id, correlation, now);
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "CONDONAR_CREDITO",
            HashSolicitud = fingerprint, Entidad = nameof(Credito), EntidadId = credit.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation,
            FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.CreditForgiven,
            nameof(Credito), credit.Id,
            new { CreditId = credit.Id, From = previousState, To = "ANULADO",
                ForgivenAmount = forgivenAmount, AuthorizationId = authorization.Id,
                AuthorizationMode = "ADMINISTRATOR_SELF", CorrelationId = correlation },
            correlation, actor.UserId, null, terminal.Id, reason, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }

    private async Task<(Usuario? User, ApplicationError? Error)> ResolveCancellationAuthorizerAsync(
        ActorContext actor, UserAuthorizationSnapshot actorSnapshot,
        CreditLifecycleAuthorizationRequest? request, bool administratorSelf, Guid terminalId,
        Guid creditId, CancellationToken cancellationToken)
    {
        if (administratorSelf)
        {
            var administrator = await sales.GetUserForUpdateAsync(actor.UserId, cancellationToken);
            return administrator is null || !administrator.IsActive
                ? (null, ApplicationError.Forbidden()) : (administrator, null);
        }
        var candidate = await users.GetByUserNameAsync(request!.UserName!.Trim(), cancellationToken);
        var authorizer = candidate is null ? null : await sales.GetUserForUpdateAsync(candidate.Id, cancellationToken);
        var snapshot = authorizer is null ? null :
            await users.GetAuthorizationSnapshotAsync(authorizer.Id, cancellationToken);
        if (authorizer is null || snapshot is null || !authorizer.IsActive ||
            authorizer.Id == actor.UserId || actorSnapshot.HighestRoleLevel != RoleLevel.Supervisor ||
            snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.CreditManage, StringComparer.OrdinalIgnoreCase))
            return (null, ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        if (!request.UsesCredential() && !passwordHasher.Verify(request.Password!, authorizer.PasswordHash))
        {
            var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
            await users.UpdateAsync(authorizer, cancellationToken);
            if (locked)
                await AuditWriter.WriteAsync(audits, clock, authorizer.Id, AuditAction.UserLocked,
                    nameof(Usuario), authorizer.Id,
                    new { authorizer.Id, authorizer.FailedLoginAttempts }, cancellationToken);
            await AuditWriter.WriteCashFailureAsync(audits, clock, actor.UserId,
                AuditAction.CreditPaymentCancellationAuthorizationRejected, nameof(PagoCredito), null,
                new { CreditId = creditId, Result = "INVALID_AUTHORIZATION" }, Guid.CreateVersion7(),
                terminalId, null, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return (null, ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        }
        authorizer.RegisterSuccessfulLogin(clock.UtcNow);
        await users.UpdateAsync(authorizer, cancellationToken);
        return (authorizer, null);
    }

    private async Task<UserAuthorizationSnapshot?> GetLifecycleActorAsync(ActorContext actor,
        RoleLevel minimumRole, CancellationToken cancellationToken)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        return snapshot is not null && snapshot.HighestRoleLevel >= minimumRole &&
               snapshot.PermissionCodes.Contains(PermissionCodes.CreditManage,
                   StringComparer.OrdinalIgnoreCase) ? snapshot : null;
    }

    private async Task<(CreditLifecycleAuthorizationRequest? Authorization, ApplicationError? Error)>
        ResolveAuthorizationAsync(CreditLifecycleAuthorizationRequest? authorization,
            CancellationToken cancellationToken)
    {
        if (authorization is null || !authorization.UsesCredential()) return (authorization, null);
        if (authorization.MixesCredentialWithPassword())
            return (null, ApplicationError.Validation(
                "La credencial escaneada reemplaza el usuario y la contraseña."));
        var match = await credentialResolver.ResolveAsync(authorization.CredentialToken!, cancellationToken);
        return (authorization with
        {
            UserName = match?.UserName ?? AuthorizerCredentialResolver.UnknownUserName,
            ResolvedCredentialId = match?.CredentialId ?? Guid.Empty
        }, null);
    }

    private static ApplicationError? ValidateCancellation(Guid creditId, Guid paymentId,
        string idempotencyKey, CancelCreditPaymentRequest request)
    {
        if (creditId == Guid.Empty || paymentId == Guid.Empty || request is null ||
            request.ExpectedVersion <= 0 || request.ExpectedVersion == long.MaxValue ||
            request.ReasonId == Guid.Empty || request.Observation?.Length > 500 ||
            !ValidKey(idempotencyKey?.Trim() ?? string.Empty) ||
            (request.Authorization is not null && !request.Authorization.IsComplete()))
            return ApplicationError.Validation(
                "La solicitud de anulación o Idempotency-Key es inválida.");
        return null;
    }

    private static bool ValidKey(string key) => key.Length is >= 1 and <= 100 &&
        key.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.' or ':');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Result<T> Replay<T>(OperacionIdempotente previous, Guid actorId,
        string operationType, Guid entityId, string fingerprint, string unavailableMessage)
    {
        if (previous.UsuarioId != actorId || previous.TipoOperacion != operationType ||
            previous.HashSolicitud != fingerprint || previous.EntidadId != entityId ||
            string.IsNullOrEmpty(previous.Resultado))
            return Result.Failure<T>(ApplicationError.Conflict(
                "La clave de idempotencia ya fue utilizada con otra solicitud."));
        try
        {
            var saved = JsonSerializer.Deserialize<T>(previous.Resultado);
            return saved is null ? Result.Failure<T>(ApplicationError.Conflict(unavailableMessage)) :
                Result.Success(saved);
        }
        catch (JsonException)
        {
            return Result.Failure<T>(ApplicationError.Conflict(unavailableMessage));
        }
    }

    private static bool IsApprovedPaymentCancellationReason(MotivoOperacion reason)
    {
        var index = Array.FindIndex(PaymentCancellationCatalog, x => x.Code == reason.Codigo);
        return index >= 0 && reason.Activo &&
               reason.TipoOperacion == ApprovedReasonCatalogs.SalePaymentCancellationType &&
               reason.Nombre == PaymentCancellationCatalog[index].Name &&
               reason.Descripcion == PaymentCancellationCatalog[index].Name &&
               reason.OrdenVisual == (index + 1) * 10;
    }
}
