using ControlPlus.Domain.Common;

namespace ControlPlus.Domain.Security;

/// <summary>
/// Current assignment of a role to a user. Its identity is the UserId/RoleId pair.
/// </summary>
public sealed class UserRole
{
    private UserRole()
    {
    }

    internal UserRole(User user, Role role, DateTimeOffset assignedAtUtc, Guid? assignedByUserId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);

        UserId = DomainGuard.RequiredId(user.Id, nameof(user));
        RoleId = DomainGuard.RequiredId(role.Id, nameof(role));
        User = user;
        Role = role;
        AssignedAtUtc = DomainGuard.Utc(assignedAtUtc, nameof(assignedAtUtc));
        AssignedByUserId = DomainGuard.OptionalId(assignedByUserId, nameof(assignedByUserId));
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public DateTimeOffset AssignedAtUtc { get; private set; }

    public Guid? AssignedByUserId { get; private set; }

    public User User { get; private set; } = null!;

    public Role Role { get; private set; } = null!;
}
