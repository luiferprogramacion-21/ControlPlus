using ControlPlus.Application.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Security.Contracts;

public sealed record AuditQuery(
    Guid? ActorUserId = null,
    AuditAction? Action = null,
    string? EntityType = null,
    Guid? EntityId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditRecordDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    AuditAction Action,
    string EntityType,
    Guid? EntityId,
    string? Details,
    string? CorrelationId,
    Guid? AuthorizerUserId,
    Guid? OperatorSessionId,
    Guid? TerminalId,
    string? Reason,
    string Result);

public interface IAuditQueryService
{
    Task<Result<PagedResult<AuditRecordDto>>> QueryAsync(
        ActorContext actor,
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
