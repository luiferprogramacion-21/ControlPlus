using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Usuario = ControlPlus.Domain.OfficialModel.Usuario;

namespace ControlPlus.Application.Security.Services;

/// <summary>
/// Valida al segundo usuario que autoriza una operación (usuario y contraseña, o carné ya resuelto por
/// <see cref="IAuthorizerCredentialResolver"/>): otro usuario, activo, de rol mayor al de quien opera, con el permiso pedido
/// y, si se indica, con el rol exacto. Una contraseña errada cuenta como intento fallido (y puede bloquear la cuenta), igual
/// que al iniciar sesión. Debe correr dentro de <see cref="ILoginAttemptCoordinator.ExecuteAsync{T}"/>.
/// </summary>
public static class AuthorizerValidator
{
    public static async Task<Result<Usuario>> ValidateAsync(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IPermissionChecker permissionChecker,
        IAuditRepository auditRepository,
        IClock clock,
        ActorContext actor,
        IAuthorizerCredentials? request,
        string requiredPermission,
        RoleLevel? requiredRole,
        ApplicationError invalid,
        CancellationToken cancellationToken)
    {
        if (request is null || !request.IsComplete()) return Result.Failure<Usuario>(invalid);
        var user = await userRepository.GetByUserNameAsync(request.UserName!, cancellationToken);
        if (user is null || !user.IsActive || user.Id == actor.UserId) return Result.Failure<Usuario>(invalid);
        var actorSnapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (actorSnapshot is null || !RoleHierarchy.IsHigherThan(user.HighestRoleLevel, actorSnapshot.HighestRoleLevel) ||
            (requiredRole is { } role && user.HighestRoleLevel != role))
            return Result.Failure<Usuario>(invalid);
        if (!request.UsesCredential() && !passwordHasher.Verify(request.Password!, user.PasswordHash))
        {
            var wasLocked = user.RegisterFailedLogin(clock.UtcNow);
            await userRepository.UpdateAsync(user, cancellationToken);
            if (wasLocked)
                await AuditWriter.WriteAsync(auditRepository, clock, user.Id, AuditAction.UserLocked, nameof(Usuario), user.Id,
                    new { user.UserName, FailedLoginAttempts = user.FailedLoginAttempts }, cancellationToken);
            return Result.Failure<Usuario>(invalid);
        }
        return await permissionChecker.HasPermissionAsync(user.Id, requiredPermission, cancellationToken)
            ? Result.Success(user)
            : Result.Failure<Usuario>(invalid);
    }
}
