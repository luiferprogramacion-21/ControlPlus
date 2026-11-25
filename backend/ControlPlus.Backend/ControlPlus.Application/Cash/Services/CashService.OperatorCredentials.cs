using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Cash.Services;

public sealed partial class CashService
{
    public async Task<Result<OperatorCredentialDto?>> GetOperatorCredentialAsync(
        ActorContext actor, Guid userId, CancellationToken cancellationToken = default)
    {
        var error = await RequireCredentialAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<OperatorCredentialDto?>(error);
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
            return Result.Failure<OperatorCredentialDto?>(ApplicationError.NotFound("el usuario"));
        var credential = await repository.GetActiveCredentialForUserAsync(userId, cancellationToken: cancellationToken);
        if (credential is null) return Result.Success<OperatorCredentialDto?>(null);
        var token = await credentialStore.ReadAsync(userId, credential.Id, cancellationToken);
        // A protected copy is useful only when it still matches the canonical credential hash.
        if (token is not null && !passwordHasher.Verify(token, credential.TokenHash)) token = null;
        return Result.Success<OperatorCredentialDto?>(new OperatorCredentialDto(
            credential.Id, userId, credential.FormatoCodigo, credential.FragmentoVisible,
            Utc(credential.FechaEmision), OptionalUtc(credential.FechaVencimiento),
            credential.FechaVencimiento is not null && credential.FechaVencimiento <= clock.UtcNow.UtcDateTime
                ? "VENCIDA" : credential.Estado,
            credential.Version, token));
    }

    public async Task<Result> RevokeOperatorCredentialAsync(
        ActorContext actor, Guid userId, Guid credentialId, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireCredentialAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure(error);
        if (credentialId == Guid.Empty || expectedVersion <= 0)
            return Result.Failure(Validation("Indique el carné y su versión para eliminarlo."));
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return Result.Failure(ApplicationError.NotFound("el usuario"));

        // Same account coordinator as sensitive authorizations; then the usual shift -> user order.
        return await loginAttemptCoordinator.ExecuteAsync(user.NombreUsuarioNormalizado, async token =>
        {
            var currentPermission = await RequireCredentialAdministratorAsync(actor, token);
            if (currentPermission is not null) return Result.Failure(currentPermission);
            var shift = await repository.GetOpenShiftAsync(true, token);
            if (await repository.GetUserForUpdateAsync(userId, token) is null)
                return Result.Failure(ApplicationError.NotFound("el usuario"));
            var credential = await repository.GetActiveCredentialForUserAsync(userId, cancellationToken: token);
            if (credential is null) return Result.Success();
            if (credential.Id != credentialId || credential.Version != expectedVersion)
                return Result.Failure(ApplicationError.Conflict("El carné cambió. Cierra esta ventana y vuelve a abrirlo antes de eliminarlo."));

            var now = clock.UtcNow;
            var correlationId = Guid.CreateVersion7();
            credential.Revoke(actor.UserId, now, "ELIMINACION_ADMINISTRADOR");
            await credentialStore.RemoveAsync(userId, credential.Id, token);
            if (shift is not null)
            {
                var sessions = await repository.ListActiveOperatorSessionsAsync(shift.Id, token);
                foreach (var session in sessions.Where(x => x.CredencialUsuarioId == credentialId))
                {
                    // MANUAL is an existing official close reason; the audit explains the revocation.
                    session.Close(now, "MANUAL");
                    await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorSessionClosed,
                        nameof(SesionOperador), session.Id, new { session.UsuarioId, session.TurnoCajaId, CredentialRevoked = true },
                        correlationId, null, session.Id, session.TerminalId, "CREDENCIAL_REVOCADA", token);
                }
            }
            await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorCredentialRevoked,
                nameof(CredencialUsuario), credential.Id,
                new { credential.UsuarioId, credential.FormatoCodigo, credential.FragmentoVisible },
                correlationId, null, null, null, "ELIMINACION_ADMINISTRADOR", token);
            await unitOfWork.SaveChangesAsync(token);
            return Result.Success();
        }, cancellationToken);
    }

    private async Task<ApplicationError?> RequireCredentialAdministratorAsync(ActorContext actor, CancellationToken token)
    {
        if (actor.UserId == Guid.Empty) return ApplicationError.Unauthorized();
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, token);
        return snapshot?.HighestRoleLevel == RoleLevel.Administrador &&
               snapshot.PermissionCodes.Contains(PermissionCodes.UsersManage, StringComparer.OrdinalIgnoreCase)
            ? null : ApplicationError.Forbidden("Solo el Administrador con permiso vigente puede gestionar carnés.");
    }
}
