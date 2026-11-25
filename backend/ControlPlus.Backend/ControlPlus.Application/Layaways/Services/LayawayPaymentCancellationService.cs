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

/// <summary>Reverses one complete payment operation; never changes products or reservations.</summary>
public sealed partial class LayawayPaymentCancellationService(ILayawayPaymentCorrectionRepository correction,
    ILayawayPaymentRepository layaways, ISalesRepository sales, ICashRepository cash,
    IUserRepository users, IPermissionRepository permissions, IAuditRepository audit,
    IPasswordHasher passwords, ILoginAttemptCoordinator attempts,
    IAuthorizerCredentialResolver credentialResolver, IUnitOfWork work, IClock clock)
    : ILayawayPaymentCancellationService, ILayawayProductCorrectionService
{
    private const string Operation = "ANULAR_ABONO_APARTADO";

    public async Task<Result<IReadOnlyCollection<LayawayPaymentCancellationReasonDto>>> ReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor.UserId, cancellationToken) is null)
            return Result.Failure<IReadOnlyCollection<LayawayPaymentCancellationReasonDto>>(ApplicationError.Forbidden());
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<IReadOnlyCollection<LayawayPaymentCancellationReasonDto>>(ApplicationError.NotFound("la instalación activa"));
        var rows = await sales.GetCancellationReasonsAsync(installation.EstablecimientoId,
            ApprovedReasonCatalogs.SalePaymentCancellationType, cancellationToken);
        return Result.Success<IReadOnlyCollection<LayawayPaymentCancellationReasonDto>>(rows.Where(Approved)
            .OrderBy(r => r.OrdenVisual).Select(r => new LayawayPaymentCancellationReasonDto(r.Id, r.Codigo, r.Nombre, r.OrdenVisual)).ToArray());
    }

    public async Task<Result<CancelLayawayPaymentDto>> CancelAsync(ActorContext actor, Guid layawayId,
        Guid paymentId, string idempotencyKey, CancelLayawayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor.UserId, cancellationToken) is null) return Fail(ApplicationError.Forbidden());
        var key = idempotencyKey?.Trim() ?? "";
        if (layawayId == Guid.Empty || paymentId == Guid.Empty || request is null ||
            request.ExpectedVersion <= 0 || request.ExpectedVersion == long.MaxValue || request.ReasonId == Guid.Empty ||
            request.Observation?.Length > 500 || key.Length is < 1 or > 100 ||
            !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':') ||
            (request.Authorization is not null && !request.Authorization.IsComplete()))
            return Fail(ApplicationError.Validation("Versión, motivo, autorización o Idempotency-Key inválidos."));
        if (request.Authorization is { } auth && auth.UsesCredential())
        {
            if (auth.MixesCredentialWithPassword()) return Fail(ApplicationError.Validation("Envíe carné o usuario y contraseña, no ambos."));
            var match = await credentialResolver.ResolveAsync(auth.CredentialToken!, cancellationToken);
            request = request with { Authorization = auth with { UserName = match?.UserName ?? AuthorizerCredentialResolver.UnknownUserName,
                ResolvedCredentialId = match?.CredentialId ?? Guid.Empty } };
        }
        var observation = string.IsNullOrWhiteSpace(request.Observation) ? null : request.Observation.Trim();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { LayawayId = layawayId, PaymentId = paymentId, request.ExpectedVersion, request.ReasonId, Observation = observation, Actor = actor.UserId }))));
        var account = request.Authorization?.UserName?.Trim().ToUpperInvariant() ?? $"LAYAWAY-PAYMENT-CANCEL:{actor.UserId:N}";
        return await attempts.ExecuteAsync(account, ct => WithinAsync(actor, layawayId, paymentId, key, request, observation, fingerprint, ct), cancellationToken);
    }

    private async Task<Result<CancelLayawayPaymentDto>> WithinAsync(ActorContext actor, Guid layawayId,
        Guid paymentId, string key, CancelLayawayPaymentRequest request, string? observation, string fingerprint, CancellationToken ct)
    {
        var installation = await sales.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Fail(ApplicationError.NotFound("la instalación activa"));
        var actorSnapshot = await ActorAsync(actor.UserId, ct);
        if (actorSnapshot is null) return Fail(ApplicationError.Forbidden());
        var previous = await sales.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != Operation || previous.EntidadId != layawayId ||
                previous.HashSolicitud != fingerprint || string.IsNullOrWhiteSpace(previous.Resultado))
                return Fail(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            try { return JsonSerializer.Deserialize<CancelLayawayPaymentDto>(previous.Resultado) is { } saved ? Result.Success(saved) : Fail(ApplicationError.Conflict("La anulación no tiene un resultado recuperable.")); }
            catch (JsonException) { return Fail(ApplicationError.Conflict("La anulación no tiene un resultado recuperable.")); }
        }
        var administrator = actorSnapshot.HighestRoleLevel == RoleLevel.Administrador;
        if (administrator && request.Authorization is not null) return Fail(ApplicationError.Validation("El Administrador anula el abono sin un segundo autorizador."));
        if (!administrator && request.Authorization is null) return Fail(ApplicationError.Validation("El Supervisor requiere autorización de otro Administrador."));
        var terminal = await sales.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Fail(ApplicationError.NotFound("la terminal activa"));
        var shift = await sales.GetOpenShiftForUpdateAsync(ct);
        if (shift is null || shift.TerminalId != terminal.Id) return Fail(ApplicationError.Conflict("Abre un turno de Caja para corregir el abono."));
        SesionOperador? session = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        { if (shift.UsuarioResponsableId != actor.UserId) return Fail(ApplicationError.Forbidden("El turno individual pertenece a otro responsable.")); }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            session = await sales.GetActiveOperatorSessionForUpdateAsync(shift.Id, terminal.Id, actor.UserId, ct);
            if (session is null) return Fail(SharedShift.CardRequired());
        }
        else return Fail(ApplicationError.Conflict("La modalidad del turno no es válida."));
        var layaway = await layaways.GetForUpdateAsync(layawayId, ct);
        if (layaway is null || layaway.InstalacionId != installation.Id) return Fail(ApplicationError.NotFound("el apartado"));
        if (layaway.Version != request.ExpectedVersion) return Fail(ApplicationError.ConcurrencyConflict("El apartado cambió. Actualízalo antes de corregir."));
        if (layaway.Estado is not ("ACTIVO" or "PAGADO")) return Fail(ApplicationError.Conflict("Solo se corrigen abonos de apartados sin entregar. Un apartado entregado requiere una devolución propia."));
        var selected = await correction.PaymentForUpdateAsync(layaway.Id, paymentId, ct);
        if (selected is null) return Fail(ApplicationError.NotFound("el abono"));
        if (selected.Estado != "APLICADO" || selected.MovimientoCajaId is not Guid movementId)
            return Fail(ApplicationError.Conflict("El abono ya fue anulado o no tiene un movimiento reversible."));
        var original = await correction.MovementForUpdateAsync(movementId, ct);
        var rows = await correction.MovementPaymentsForUpdateAsync(movementId, ct);
        decimal amount;
        try { amount = CashMoney.Sum(rows.Select(p => p.ValorAplicado)); }
        catch (Exception e) when (e is CashMoneyException or OverflowException) { return Fail(ApplicationError.Conflict("Los importes del abono no son consistentes.")); }
        if (original is null || original.Tipo != "INGRESO" || original.CategoriaMovimiento is not ("APARTADO" or "ABONO_APARTADO") ||
            original.Valor != amount || amount <= 0 || rows.Count == 0 || rows.Any(p => p.ApartadoId != layaway.Id || p.Estado != "APLICADO") ||
            await correction.HasReversalAsync(movementId, ct) || layaway.SaldoPendiente > layaway.Total - amount)
            return Fail(ApplicationError.Conflict("La operación de abono no admite una reversión completa y segura."));
        var methods = await correction.MethodsAsync(rows.Select(p => p.MetodoPagoId).Distinct().ToArray(), ct);
        if (methods.Count != rows.Select(p => p.MetodoPagoId).Distinct().Count()) return Fail(ApplicationError.Conflict("Falta un medio de pago del abono."));
        var expected = await cash.GetExpectedAmountsAsync(shift.Id, ct);
        var cashRefund = CashMoney.Sum(rows.Join(methods.Where(m => m.AfectaEfectivo), p => p.MetodoPagoId, m => m.Id, (p, _) => p.ValorAplicado));
        if (cashRefund > CashMoney.Sum(expected.Where(m => m.AffectsCash).Select(m => m.ExpectedAmount)))
            return Fail(ApplicationError.Conflict("No hay efectivo suficiente en el turno para revertir este abono."));
        var reasons = await sales.GetCancellationReasonsAsync(installation.EstablecimientoId, ApprovedReasonCatalogs.SalePaymentCancellationType, ct);
        var reason = reasons.SingleOrDefault(r => r.Id == request.ReasonId && Approved(r));
        if (reason is null) return Fail(ApplicationError.Validation("Selecciona un motivo activo de anulación de pago."));
        if (reason.Codigo == "OTRO" && observation is null) return Fail(ApplicationError.Validation("Escribe una observación para el motivo Otro."));
        var permission = await permissions.GetByCodeAsync(PermissionCodes.LayawayManage, ct);
        if (permission is null || !permission.Activo) return Fail(ApplicationError.Conflict("El permiso LAYAWAY.MANAGE no está disponible."));
        var authorizer = await AuthorizerAsync(actor, request.Authorization, administrator, terminal.Id, layaway.Id, ct);
        if (authorizer.Error is not null) return Fail(authorizer.Error);
        var now = clock.UtcNow; var correlation = Guid.CreateVersion7();
        var authorization = new AutorizacionOperacion { Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.User!.Id, PermisoId = permission.Id, TipoOperacion = "ANULACION_PAGO_APARTADO",
            CorrelacionId = correlation, DescripcionOperacion = "Corrección de abono de apartado.", Motivo = reason.Codigo,
            MetodoAutenticacion = request.Authorization.AuthenticationMethod(), CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId,
            Estado = "UTILIZADA", Resultado = "EXITOSA", FechaSolicitud = now.UtcDateTime, FechaUtilizacion = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime };
        await sales.AddAuthorizationAsync(authorization, ct);
        foreach (var payment in rows)
        { payment.Estado = "ANULADO"; payment.UsuarioAnulacionId = actor.UserId; payment.AutorizacionAnulacionId = authorization.Id;
            payment.MotivoAnulacionId = reason.Id; payment.ObservacionAnulacion = observation; payment.FechaAnulacion = now.UtcDateTime; }
        layaway.SaldoPendiente += amount; layaway.Estado = "ACTIVO"; layaway.FechaPago = null;
        await sales.AddCashMovementAsync(new MovimientoCaja { Id = Guid.CreateVersion7(), TurnoCajaId = shift.Id,
            UsuarioId = actor.UserId, SesionOperadorId = session?.Id, Tipo = "REVERSO", CategoriaMovimiento = "REVERSO_PAGO",
            MovimientoRevertidoId = movementId, Valor = amount, Concepto = "Corrección de abono de apartado", FechaHora = now.UtcDateTime, CorrelacionId = correlation }, ct);
        var response = new CancelLayawayPaymentDto(layaway.Id, layaway.SaldoPendiente, layaway.Estado,
            checked(request.ExpectedVersion + 1), amount, correlation, rows.Select(p => p.Id).ToArray());
        await sales.AddIdempotencyAsync(new OperacionIdempotente { Id = Guid.CreateVersion7(), InstalacionId = installation.Id,
            TerminalId = terminal.Id, UsuarioId = actor.UserId, SesionOperadorId = session?.Id, ClaveIdempotencia = key,
            TipoOperacion = Operation, HashSolicitud = fingerprint, Entidad = nameof(Apartado), EntidadId = layaway.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation, FechaRegistro = now.UtcDateTime }, ct);
        await AuditWriter.WriteCashAsync(audit, clock, actor.UserId, AuditAction.LayawayPaymentCancelled,
            nameof(PagoApartado), selected.Id, new { LayawayId = layaway.Id, PaymentIds = rows.Select(p => p.Id).ToArray(),
                ReversedAmount = amount, AuthorizationId = authorization.Id, OriginalShiftId = original.TurnoCajaId,
                ReversalShiftId = shift.Id, AuthorizationMode = administrator ? "ADMINISTRATOR_SELF" : "OTHER_USER" },
            correlation, authorizer.User.Id, session?.Id, terminal.Id, reason.Codigo, ct);
        await work.SaveChangesAsync(ct);
        if (session is not null) { session.Close(now.UtcDateTime, "CAMBIO_OPERADOR"); await work.SaveChangesAsync(ct); }
        return Result.Success(response);
    }

    private async Task<(Usuario? User, ApplicationError? Error)> AuthorizerAsync(ActorContext actor,
        LayawayPaymentCancellationAuthorization? auth, bool administrator, Guid terminalId, Guid layawayId, CancellationToken ct)
    {
        if (administrator)
        { var self = await sales.GetUserForUpdateAsync(actor.UserId, ct); return self is { IsActive: true } ? (self, null) : (null, ApplicationError.Forbidden()); }
        var candidate = await users.GetByUserNameAsync(auth!.UserName!.Trim(), ct);
        var user = candidate is null ? null : await sales.GetUserForUpdateAsync(candidate.Id, ct);
        var snapshot = user is null ? null : await users.GetAuthorizationSnapshotAsync(user.Id, ct);
        if (user is null || snapshot is null || !user.IsActive || user.Id == actor.UserId || snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase))
            return (null, ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        if (!auth.UsesCredential() && !passwords.Verify(auth.Password!, user.PasswordHash))
        {
            var locked = user.RegisterFailedLogin(clock.UtcNow); await users.UpdateAsync(user, ct);
            if (locked) await AuditWriter.WriteAsync(audit, clock, user.Id, AuditAction.UserLocked, nameof(Usuario), user.Id, new { user.Id }, ct);
            await AuditWriter.WriteCashFailureAsync(audit, clock, actor.UserId, AuditAction.LayawayPaymentCancellationAuthorizationRejected,
                nameof(PagoApartado), null, new { LayawayId = layawayId, Result = "INVALID_AUTHORIZATION" }, Guid.CreateVersion7(), terminalId, null, ct);
            await work.SaveChangesAsync(ct); return (null, ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        }
        user.RegisterSuccessfulLogin(clock.UtcNow); await users.UpdateAsync(user, ct); return (user, null);
    }

    private async Task<UserAuthorizationSnapshot?> ActorAsync(Guid id, CancellationToken ct)
    { var snapshot = await users.GetAuthorizationSnapshotAsync(id, ct); return snapshot is not null && snapshot.HighestRoleLevel >= RoleLevel.Supervisor &&
        snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase) ? snapshot : null; }
    private static bool Approved(MotivoOperacion reason)
    { var index = Array.FindIndex(ApprovedReasonCatalogs.SaleCancellation, r => r.Code == reason.Codigo);
        return index >= 0 && reason.Activo && reason.TipoOperacion == ApprovedReasonCatalogs.SalePaymentCancellationType &&
            reason.Nombre == ApprovedReasonCatalogs.SaleCancellation[index].Name && reason.Descripcion == reason.Nombre && reason.OrdenVisual == (index + 1) * 10; }
    private static Result<CancelLayawayPaymentDto> Fail(ApplicationError error) => Result.Failure<CancelLayawayPaymentDto>(error);
}
