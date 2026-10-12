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

    public static Task WriteSaleAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid actorUserId,
        Guid saleId,
        object details,
        Guid correlationId,
        Guid? operatorSessionId,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        var serializedDetails = JsonSerializer.Serialize(details);
        var auditRecord = AuditRecord.Create(
            actorUserId,
            AuditAction.SaleConfirmed,
            "Venta",
            saleId,
            serializedDetails,
            clock.UtcNow,
            correlationId.ToString(),
            operatorSessionId: operatorSessionId,
            terminalId: terminalId);
        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }

    public static Task WriteCreditAuthorizationFailureAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid actorUserId,
        Guid correlationId,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        var auditRecord = AuditRecord.Create(actorUserId,
            AuditAction.CreditAuthorizationRejected, "Credito", null,
            JsonSerializer.Serialize(new { Reason = "INVALID_AUTHORIZER_CREDENTIALS" }),
            clock.UtcNow, correlationId.ToString(), terminalId: terminalId, result: "FALLIDO");
        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }

    public static Task WriteInventoryAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid actorUserId,
        Guid authorizerUserId,
        Guid movementId,
        string reason,
        object details,
        Guid correlationId,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        var serializedDetails = JsonSerializer.Serialize(details);
        var auditRecord = AuditRecord.Create(
            actorUserId,
            AuditAction.InventoryAdjusted,
            "MovimientoInventario",
            movementId,
            serializedDetails,
            clock.UtcNow,
            correlationId.ToString(),
            authorizerUserId,
            terminalId: terminalId,
            reason: reason);
        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }

    public static Task WriteInventoryFailureAsync(
        IAuditRepository auditRepository,
        IClock clock,
        Guid actorUserId,
        Guid productId,
        object details,
        Guid correlationId,
        Guid? authorizerUserId,
        Guid terminalId,
        CancellationToken cancellationToken)
    {
        var serializedDetails = JsonSerializer.Serialize(details);
        var auditRecord = AuditRecord.Create(
            actorUserId,
            AuditAction.InventoryAdjustmentAuthorizationRejected,
            "Producto",
            productId,
            serializedDetails,
            clock.UtcNow,
            correlationId.ToString(),
            authorizerUserId,
            terminalId: terminalId,
            result: "FALLIDO");
        return auditRepository.AddAsync(auditRecord, cancellationToken);
    }
}
