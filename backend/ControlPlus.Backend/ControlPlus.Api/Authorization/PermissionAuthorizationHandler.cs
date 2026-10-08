using System.IdentityModel.Tokens.Jwt;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace ControlPlus.Api.Authorization;

/// <summary>
/// Resolves permissions from the current database state rather than from JWT claims.
/// </summary>
public sealed class PermissionAuthorizationHandler(IPermissionChecker permissionChecker)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return;
        }

        foreach (var permission in requirement.Permission.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (await permissionChecker.HasPermissionAsync(userId, permission))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
