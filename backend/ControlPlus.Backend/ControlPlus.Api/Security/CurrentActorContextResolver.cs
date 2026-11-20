using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Api.Security;

/// <summary>
/// Builds an actor context from the current persisted authorization snapshot.
/// This avoids treating privilege data embedded in an access token as current.
/// </summary>
public sealed class CurrentActorContextResolver(IUserRepository userRepository, ICashRepository cashRepository,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<ActorContext?> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(userId, cancellationToken);
        if (snapshot is null) return null;

        // Shared shift (20 November 2026): an operation marked [OperatorScoped] acts as the employee whose card session is
        // active on the terminal. Their own snapshot is used, so their role, permissions and limits apply. If that
        // employee is no longer active, the logged-in user stays the actor and the operation is refused as before.
        if (IsOperatorScoped() &&
            await cashRepository.GetActiveSharedOperatorUserIdAsync(cancellationToken) is Guid operatorId &&
            operatorId != snapshot.UserId &&
            await userRepository.GetAuthorizationSnapshotAsync(operatorId, cancellationToken) is { } operatorSnapshot)
            snapshot = operatorSnapshot;

        return new ActorContext(snapshot.UserId, snapshot.HighestRoleLevel, snapshot.PermissionCodes);
    }

    private bool IsOperatorScoped()
    {
        var context = httpContextAccessor.HttpContext;
        var marker = context?.GetEndpoint()?.Metadata.GetMetadata<OperatorScopedAttribute>();
        if (marker is null) return false;
        return marker.IncludeReads || !(HttpMethods.IsGet(context!.Request.Method) || HttpMethods.IsHead(context.Request.Method));
    }
}
