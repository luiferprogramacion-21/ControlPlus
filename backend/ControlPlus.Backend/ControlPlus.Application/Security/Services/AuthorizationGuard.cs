using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Security.Services;

internal static class AuthorizationGuard
{
    public static async Task<ApplicationError?> RequirePermissionAsync(
        IPermissionChecker permissionChecker,
        ActorContext actor,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        if (actor.UserId == Guid.Empty)
        {
            return ApplicationError.Unauthorized();
        }

        return await permissionChecker.HasPermissionAsync(actor.UserId, permissionCode, cancellationToken)
            ? null
            : ApplicationError.Forbidden();
    }
}
