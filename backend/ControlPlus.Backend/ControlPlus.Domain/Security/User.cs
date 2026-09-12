using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Security;

public sealed class User
{
    public const int MaximumFailedLoginAttempts = 5;

    private readonly List<UserRole> _userRoles = [];

    private User()
    {
    }

    private User(
        Guid id,
        string userName,
        string displayName,
        string passwordHash,
        DateTimeOffset createdAtUtc)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        UserName = DomainGuard.RequiredText(userName, nameof(userName));
        NormalizedUserName = UserName.ToUpperInvariant();
        DisplayName = DomainGuard.RequiredText(displayName, nameof(displayName));
        PasswordHash = DomainGuard.RequiredText(passwordHash, nameof(passwordHash));
        SecurityStamp = CreateSecurityStamp();
        Status = UserStatus.Active;
        CreatedAtUtc = DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; } = null!;

    public string NormalizedUserName { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// Changes whenever existing credentials or authorization should stop being trusted.
    /// </summary>
    public string SecurityStamp { get; private set; } = null!;

    public UserStatus Status { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public bool IsLocked => Status == UserStatus.Locked;

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockedAtUtc { get; private set; }

    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    /// <summary>
    /// The highest active role assigned to this user. A user without an active role has no authority.
    /// </summary>
    public RoleLevel HighestRoleLevel
    {
        get
        {
            var highestLevel = RoleLevel.None;

            foreach (var userRole in _userRoles)
            {
                if (userRole.Role is { IsActive: true } role && role.Level > highestLevel)
                {
                    highestLevel = role.Level;
                }
            }

            return highestLevel;
        }
    }

    private RoleLevel HighestAssignedRoleLevel
    {
        get
        {
            var highestLevel = RoleLevel.None;

            foreach (var userRole in _userRoles)
            {
                if (userRole.Role is { } role && role.Level > highestLevel)
                {
                    highestLevel = role.Level;
                }
            }

            return highestLevel;
        }
    }

    public static User Create(
        string userName,
        string displayName,
        string passwordHash,
        DateTimeOffset createdAtUtc) =>
        new(Guid.CreateVersion7(), userName, displayName, passwordHash, createdAtUtc);

    public bool CanBeReactivatedBy(RoleLevel actingRoleLevel) =>
        IsLocked &&
        RoleHierarchy.IsDefined(actingRoleLevel) &&
        RoleHierarchy.IsHigherThan(actingRoleLevel, HighestAssignedRoleLevel);

    /// <summary>
    /// Registers a failed credential validation. Returns true only when this attempt locks the user.
    /// Attempts against inactive or already locked users do not mutate the aggregate.
    /// </summary>
    public bool RegisterFailedLogin(DateTimeOffset occurredAtUtc)
    {
        occurredAtUtc = DomainGuard.Utc(occurredAtUtc, nameof(occurredAtUtc));

        if (!IsActive)
        {
            return false;
        }

        FailedLoginAttempts++;
        UpdatedAtUtc = occurredAtUtc;

        if (FailedLoginAttempts < MaximumFailedLoginAttempts)
        {
            return false;
        }

        Status = UserStatus.Locked;
        LockedAtUtc = occurredAtUtc;
        RotateSecurityStamp();
        return true;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset occurredAtUtc)
    {
        occurredAtUtc = DomainGuard.Utc(occurredAtUtc, nameof(occurredAtUtc));

        if (!IsActive)
        {
            throw new DomainRuleViolationException("Only active users can sign in.");
        }

        FailedLoginAttempts = 0;
        LastLoginAtUtc = occurredAtUtc;
        UpdatedAtUtc = occurredAtUtc;
    }

    public void ChangePasswordHash(string passwordHash, DateTimeOffset changedAtUtc)
    {
        PasswordHash = DomainGuard.RequiredText(passwordHash, nameof(passwordHash));
        UpdatedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));
        RotateSecurityStamp();
    }

    public void UpdateDisplayName(string displayName, DateTimeOffset updatedAtUtc)
    {
        DisplayName = DomainGuard.RequiredText(displayName, nameof(displayName));
        UpdatedAtUtc = DomainGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void Activate(DateTimeOffset changedAtUtc)
    {
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        if (IsLocked)
        {
            throw new DomainRuleViolationException("A locked user must be reactivated by a higher role.");
        }

        if (IsActive)
        {
            return;
        }

        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedAtUtc = null;
        UpdatedAtUtc = changedAtUtc;
        RotateSecurityStamp();
    }

    public void Deactivate(DateTimeOffset changedAtUtc)
    {
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        if (IsLocked)
        {
            throw new DomainRuleViolationException("A locked user must be reactivated before its status can change.");
        }

        if (Status == UserStatus.Inactive)
        {
            return;
        }

        Status = UserStatus.Inactive;
        UpdatedAtUtc = changedAtUtc;
        RotateSecurityStamp();
    }

    /// <summary>
    /// Unlocks a user only when the actor has a strictly higher active authority level.
    /// </summary>
    public void Reactivate(RoleLevel actingRoleLevel, DateTimeOffset changedAtUtc)
    {
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        if (!IsLocked)
        {
            throw new DomainRuleViolationException("Only locked users can be reactivated.");
        }

        if (!CanBeReactivatedBy(actingRoleLevel))
        {
            throw new DomainRuleViolationException("A strictly higher role is required to reactivate this user.");
        }

        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedAtUtc = null;
        UpdatedAtUtc = changedAtUtc;
        RotateSecurityStamp();
    }

    public bool AddRole(Role role, DateTimeOffset assignedAtUtc, Guid? assignedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        assignedAtUtc = DomainGuard.Utc(assignedAtUtc, nameof(assignedAtUtc));

        if (!role.IsActive)
        {
            throw new DomainRuleViolationException("An inactive role cannot be assigned.");
        }

        if (_userRoles.Any(userRole => userRole.RoleId == role.Id))
        {
            return false;
        }

        _userRoles.Add(new UserRole(this, role, assignedAtUtc, assignedByUserId));
        UpdatedAtUtc = assignedAtUtc;
        RotateSecurityStamp();
        return true;
    }

    public bool RemoveRole(Guid roleId, DateTimeOffset changedAtUtc)
    {
        roleId = DomainGuard.RequiredId(roleId, nameof(roleId));
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        var userRole = _userRoles.SingleOrDefault(candidate => candidate.RoleId == roleId);
        if (userRole is null)
        {
            return false;
        }

        _userRoles.Remove(userRole);
        UpdatedAtUtc = changedAtUtc;
        RotateSecurityStamp();
        return true;
    }

    public string RotateSecurityStamp()
    {
        SecurityStamp = CreateSecurityStamp();
        return SecurityStamp;
    }

    private static string CreateSecurityStamp() => Guid.CreateVersion7().ToString("N");
}
