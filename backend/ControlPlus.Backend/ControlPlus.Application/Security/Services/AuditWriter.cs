using System.Text.Json;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Security.Services;

internal static class AuditWriter
{
    public static Task WriteAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid? actorUserId,
        AuditAction action,
        string entityType,
        Guid? entityId,
        object? details,
        CancellationToken cancellationToken)
    {
        var serializedDetails = details is null ? null : JsonSerializer.Serialize(details);
        var auditRecord = AuditRecord.Create(
            actorUserId,
            action,
            entityType,
            entityId,
            serializedDetails,
            clock.UtcNow);

        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }

    public static Task WriteCashAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid actorUserId,
        AuditAction action,
        string entityType,
        Guid entityId,
        object? details,
        Guid correlationId,
        Guid? authorizerUserId,
        Guid? operatorSessionId,
        Guid? terminalId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var serializedDetails = details is null ? null : JsonSerializer.Serialize(details);
        var auditRecord = AuditRecord.Create(
            actorUserId,
            action,
            entityType,
            entityId,
            serializedDetails,
            clock.UtcNow,
            correlationId.ToString(),
            authorizerUserId,
            operatorSessionId,
            terminalId,
            reason);

        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }

    public static Task WriteCashFailureAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid actorUserId,
        AuditAction action,
        string entityType,
        Guid entityId,
        object? details,
        Guid correlationId,
        Guid? terminalId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var serializedDetails = details is null ? null : JsonSerializer.Serialize(details);
        var auditRecord = AuditRecord.Create(
            actorUserId,
            action,
            entityType,
            entityId,
            serializedDetails,
            clock.UtcNow,
            correlationId.ToString(),
            terminalId: terminalId,
            reason: reason,
            result: "FALLIDO");

        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }
}
