using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Inventory.Contracts;

public sealed record InventoryStockQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 50,
    // Solo productos activos con disponible igual o menor al mínimo (el mismo criterio del Dashboard).
    bool LowStock = false);

public sealed record InventoryStockDto(
    Guid ProductId,
    string InternalCode,
    string? Barcode,
    string Name,
    string MeasurementUnit,
    int CurrentStock,
    int ReservedStock,
    int AvailableStock,
    int MinimumStock,
    bool IsOutOfStock,
    bool IsActive,
    decimal? AverageCost,
    string? LastMovementType,
    DateTimeOffset? LastMovementAtUtc,
    long Version);

public sealed record InventoryMovementQuery(
    Guid? ProductId = null,
    string? Type = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record InventoryMovementDto(
    Guid Id,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UserId,
    Guid? AuthorizationId,
    Guid? ReasonId,
    string? ReasonCode,
    string? ReasonName,
    Guid? SaleId,
    Guid? PurchaseId,
    string Type,
    int StockQuantity,
    int ReservedQuantity,
    int PreviousStock,
    int NewStock,
    int PreviousReserved,
    int NewReserved,
    decimal? PreviousAverageCost,
    decimal? NewAverageCost,
    string? Observation,
    DateTimeOffset OccurredAtUtc,
    Guid CorrelationId);

public sealed record InventoryAdjustmentReasonDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryAdjustmentAuthorizationRequest(string? UserName, string? Password, string? CredentialToken = null)
    : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateInventoryAdjustmentRequest(
    Guid ProductId,
    int QuantityDelta,
    Guid ReasonId,
    long ExpectedVersion,
    string? Observation,
    InventoryAdjustmentAuthorizationRequest? Authorization = null);

public interface IInventoryService
{
    Task<Result<PagedResult<InventoryStockDto>>> ListStockAsync(
        ActorContext actor,
        InventoryStockQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<InventoryMovementDto>>> ListMovementsAsync(
        ActorContext actor,
        InventoryMovementQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<InventoryAdjustmentReasonDto>>> ListAdjustmentReasonsAsync(
        ActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryMovementDto>> AdjustAsync(
        ActorContext actor,
        string idempotencyKey,
        CreateInventoryAdjustmentRequest request,
        CancellationToken cancellationToken = default);
}
