using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Security;

public sealed class Role
{
    private readonly List<RolePermission> _rolePermissions = [];

    private Role()
    {
    }

    private Role(Guid id, string code, string name, RoleLevel level, DateTimeOffset createdAtUtc)
    {
        if (!RoleHierarchy.IsDefined(level))
        {
            throw new DomainRuleViolationException("A valid role authority level is required.");
        }

        Id = DomainGuard.RequiredId(id, nameof(id));
        Code = DomainGuard.NormalizeCode(code, nameof(code));
        Name = DomainGuard.RequiredText(name, nameof(name));
        Level = level;
        IsActive = true;
        CreatedAtUtc = DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Stable authorization identifier. It is normalized to uppercase and intentionally immutable.
    /// </summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public RoleLevel Level { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();

    public static Role Create(string code, string name, RoleLevel level, DateTimeOffset createdAtUtc) =>
        new(Guid.CreateVersion7(), code, name, level, createdAtUtc);

    public void Update(string name, RoleLevel level, DateTimeOffset updatedAtUtc)
    {
        if (!RoleHierarchy.IsDefined(level))
        {
            throw new DomainRuleViolationException("A valid role authority level is required.");
        }

        Name = DomainGuard.RequiredText(name, nameof(name));
        Level = level;
        UpdatedAtUtc = DomainGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void Activate(DateTimeOffset changedAtUtc)
    {
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAtUtc = changedAtUtc;
    }

    public void Deactivate(DateTimeOffset changedAtUtc)
    {
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAtUtc = changedAtUtc;
    }

    public bool HasAtLeastLevel(RoleLevel requiredLevel) =>
        IsActive && RoleHierarchy.HasAtLeast(Level, requiredLevel);

    public bool IsHigherThan(RoleLevel targetLevel) =>
        IsActive && RoleHierarchy.IsHigherThan(Level, targetLevel);

    public bool HasPermission(string permissionCode)
    {
        var normalizedPermissionCode = DomainGuard.NormalizeCode(permissionCode, nameof(permissionCode));

        return IsActive && _rolePermissions.Any(rolePermission =>
            rolePermission.Permission is { IsActive: true } permission &&
            permission.Code == normalizedPermissionCode);
    }

    public bool GrantPermission(
        Permission permission,
        DateTimeOffset grantedAtUtc,
        Guid? grantedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(permission);
        grantedAtUtc = DomainGuard.Utc(grantedAtUtc, nameof(grantedAtUtc));

        if (!permission.IsActive)
        {
            throw new DomainRuleViolationException("An inactive permission cannot be granted.");
        }

        if (_rolePermissions.Any(rolePermission => rolePermission.PermissionId == permission.Id))
        {
            return false;
        }

        _rolePermissions.Add(new RolePermission(this, permission, grantedAtUtc, grantedByUserId));
        UpdatedAtUtc = grantedAtUtc;
        return true;
    }

    public bool RevokePermission(Guid permissionId, DateTimeOffset changedAtUtc)
    {
        permissionId = DomainGuard.RequiredId(permissionId, nameof(permissionId));
        changedAtUtc = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc));

        var rolePermission = _rolePermissions.SingleOrDefault(candidate => candidate.PermissionId == permissionId);
        if (rolePermission is null)
        {
            return false;
        }

        _rolePermissions.Remove(rolePermission);
        UpdatedAtUtc = changedAtUtc;
        return true;
    }
}
