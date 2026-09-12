using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Security;

/// <summary>
/// Immutable business-audit event. Details may hold a serialized, non-secret payload.
/// </summary>
public sealed class AuditRecord
{
    private AuditRecord()
    {
    }

    private AuditRecord(
        Guid id,
        Guid? actorUserId,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? details,
        DateTimeOffset occurredAtUtc,
        string? correlationId)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "A defined audit action is required.");
        }

        Id = DomainGuard.RequiredId(id, nameof(id));
        ActorUserId = DomainGuard.OptionalId(actorUserId, nameof(actorUserId));
        Action = action;
        EntityType = DomainGuard.RequiredText(entityType, nameof(entityType));
        EntityId = DomainGuard.OptionalId(entityId, nameof(entityId));
        Details = NormalizeOptionalText(details);
        OccurredAtUtc = DomainGuard.Utc(occurredAtUtc, nameof(occurredAtUtc));
        CorrelationId = NormalizeOptionalText(correlationId);
    }

    public Guid Id { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public AuditAction Action { get; private set; }

    public string EntityType { get; private set; } = null!;

    public Guid? EntityId { get; private set; }

    public string? Details { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditRecord Create(
        Guid? actorUserId,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? details,
        DateTimeOffset occurredAtUtc,
        string? correlationId = null) =>
        new(
            Guid.CreateVersion7(),
            actorUserId,
            action,
            entityType,
            entityId,
            details,
            occurredAtUtc,
            correlationId);

    public static AuditRecord Restore(
        Guid id,
        Guid? actorUserId,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? details,
        DateTimeOffset occurredAtUtc,
        string? correlationId) =>
        new(id, actorUserId, action, entityType, entityId, details, occurredAtUtc, correlationId);

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
