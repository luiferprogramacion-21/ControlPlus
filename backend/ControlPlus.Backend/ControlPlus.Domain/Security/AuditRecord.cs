using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Security;

/// <summary>
/// Immutable business-audit event transported by the application layer and persisted through the official model.
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
        string? correlationId,
        Guid? authorizerUserId,
        Guid? operatorSessionId,
        Guid? terminalId,
        string? reason,
        string result)
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
        AuthorizerUserId = DomainGuard.OptionalId(authorizerUserId, nameof(authorizerUserId));
        OperatorSessionId = DomainGuard.OptionalId(operatorSessionId, nameof(operatorSessionId));
        TerminalId = DomainGuard.OptionalId(terminalId, nameof(terminalId));
        Reason = NormalizeOptionalText(reason);
        Result = result is "EXITOSO" or "FALLIDO"
            ? result
            : throw new ArgumentOutOfRangeException(nameof(result), result, "Audit result must be EXITOSO or FALLIDO.");
    }

    public Guid Id { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public AuditAction Action { get; private set; }

    public string EntityType { get; private set; } = null!;

    public Guid? EntityId { get; private set; }

    public string? Details { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string? CorrelationId { get; private set; }

    public Guid? AuthorizerUserId { get; private set; }

    public Guid? OperatorSessionId { get; private set; }

    public Guid? TerminalId { get; private set; }

    public string? Reason { get; private set; }

    public string Result { get; private set; } = "EXITOSO";

    public static AuditRecord Create(
        Guid? actorUserId,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? details,
        DateTimeOffset occurredAtUtc,
        string? correlationId = null,
        Guid? authorizerUserId = null,
        Guid? operatorSessionId = null,
        Guid? terminalId = null,
        string? reason = null,
        string result = "EXITOSO") =>
        new(
            Guid.CreateVersion7(),
            actorUserId,
            action,
            entityType,
            entityId,
            details,
            occurredAtUtc,
            correlationId,
            authorizerUserId,
            operatorSessionId,
            terminalId,
            reason,
            result);

    public static AuditRecord Restore(
        Guid id,
        Guid? actorUserId,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? details,
        DateTimeOffset occurredAtUtc,
        string? correlationId,
        Guid? authorizerUserId = null,
        Guid? operatorSessionId = null,
        Guid? terminalId = null,
        string? reason = null,
        string result = "EXITOSO") =>
        new(id, actorUserId, action, entityType, entityId, details, occurredAtUtc, correlationId,
            authorizerUserId, operatorSessionId, terminalId, reason, result);

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
