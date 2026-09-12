using ControlPlus.Domain.Common;

namespace ControlPlus.Domain.Security;

/// <summary>
/// Current assignment of a permission to a role. Its identity is the RoleId/PermissionId pair.
/// </summary>
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(Role role, Permission permission, DateTimeOffset grantedAtUtc, Guid? grantedByUserId)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permission);

        RoleId = DomainGuard.RequiredId(role.Id, nameof(role));
        PermissionId = DomainGuard.RequiredId(permission.Id, nameof(permission));
        Role = role;
        Permission = permission;
        GrantedAtUtc = DomainGuard.Utc(grantedAtUtc, nameof(grantedAtUtc));
        GrantedByUserId = DomainGuard.OptionalId(grantedByUserId, nameof(grantedByUserId));
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    public DateTimeOffset GrantedAtUtc { get; private set; }

    public Guid? GrantedByUserId { get; private set; }

    public Role Role { get; private set; } = null!;

    public Permission Permission { get; private set; } = null!;
}
