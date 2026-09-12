using ControlPlus.Domain.Common;

namespace ControlPlus.Domain.Security;

public sealed class Permission
{
    private Permission()
    {
    }

    private Permission(Guid id, string code, string name, string? description, DateTimeOffset createdAtUtc)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        Code = DomainGuard.NormalizeCode(code, nameof(code));
        Name = DomainGuard.RequiredText(name, nameof(name));
        Description = NormalizeOptionalText(description);
        IsActive = true;
        CreatedAtUtc = DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Stable authorization identifier. It is normalized to uppercase and intentionally immutable.
    /// </summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public static Permission Create(
        string code,
        string name,
        string? description,
        DateTimeOffset createdAtUtc) =>
        new(Guid.CreateVersion7(), code, name, description, createdAtUtc);

    public void Update(string name, string? description, DateTimeOffset updatedAtUtc)
    {
        Name = DomainGuard.RequiredText(name, nameof(name));
        Description = NormalizeOptionalText(description);
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

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
