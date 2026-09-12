using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Security.Contracts;

/// <summary>
/// Identity and authorization data of the user invoking a protected use case.
/// Controllers normally build it from validated JWT claims.
/// </summary>
public sealed class ActorContext
{
    private readonly HashSet<string> _permissionCodes;

    public ActorContext(
        Guid userId,
        RoleLevel highestRoleLevel,
        IEnumerable<string>? permissionCodes = null)
    {
        UserId = userId;
        HighestRoleLevel = highestRoleLevel;
        _permissionCodes = new HashSet<string>(
            permissionCodes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public Guid UserId { get; }

    public RoleLevel HighestRoleLevel { get; }

    public IReadOnlySet<string> PermissionCodes => _permissionCodes;

    public bool HasPermission(string permissionCode) => _permissionCodes.Contains(permissionCode);
}
