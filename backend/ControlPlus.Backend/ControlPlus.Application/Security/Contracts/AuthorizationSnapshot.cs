using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Security.Contracts;

/// <summary>
/// A flattened, current view of the authorization data needed to issue or validate a session.
/// Repository implementations must exclude inactive roles and permissions from its claims.
/// </summary>
public sealed record UserAuthorizationSnapshot(
    Guid UserId,
    string UserName,
    string DisplayName,
    string SecurityStamp,
    RoleLevel HighestRoleLevel,
    IReadOnlyCollection<RoleClaim> Roles,
    IReadOnlyCollection<PermissionClaim> Permissions)
{
    public IReadOnlyCollection<string> RoleCodes => Roles.Select(role => role.Code).ToArray();

    public IReadOnlyCollection<string> PermissionCodes => Permissions.Select(permission => permission.Code).ToArray();
}

public sealed record RoleClaim(Guid Id, string Code, string Name, RoleLevel Level);

public sealed record PermissionClaim(Guid Id, string Code, string Name);
