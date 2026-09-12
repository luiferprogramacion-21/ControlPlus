using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Api.Security;

/// <summary>
/// Builds an actor context from the current persisted authorization snapshot.
/// This avoids treating privilege data embedded in an access token as current.
/// </summary>
public sealed class CurrentActorContextResolver(IUserRepository userRepository)
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
        return snapshot is null
            ? null
            : new ActorContext(snapshot.UserId, snapshot.HighestRoleLevel);
    }
}
