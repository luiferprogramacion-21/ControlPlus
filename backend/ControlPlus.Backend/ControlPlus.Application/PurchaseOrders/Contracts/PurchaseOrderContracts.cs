using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.PurchaseOrders.Contracts;

public sealed record PurchaseOrderListQuery(Guid? SupplierId = null, string? Status = null,
    int Page = 1, int PageSize = 20);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseOrderLineRequest(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] int RequestedQuantity,
    decimal? EstimatedUnitCost = null,
    string? Observation = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePurchaseOrderRequest(
    [property: JsonRequired] Guid SupplierId,
    [property: JsonRequired] IReadOnlyCollection<PurchaseOrderLineRequest> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePurchaseOrderRequest(
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] IReadOnlyCollection<PurchaseOrderLineRequest> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SendPurchaseOrderRequest([property: JsonRequired] long ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseOrderAuthorizationRequest(string? UserName, string? Password, string? CredentialToken = null)
    : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClosePurchaseOrderRequest([property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] Guid ReasonId, string? Observation,
    PurchaseOrderAuthorizationRequest? Authorization = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelPurchaseOrderRequest([property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] Guid ReasonId, string? Observation,
    PurchaseOrderAuthorizationRequest? Authorization = null);

public sealed record PurchaseOrderReasonDto(Guid Id, string Code, string Name, int DisplayOrder);

public sealed record PurchaseOrderLineDto(Guid Id, Guid ProductId, string ProductCode,
    string ProductName, int RequestedQuantity, int ReceivedQuantity, int PendingQuantity,
    int ExcessQuantity, int SuggestedQuantity, decimal? EstimatedUnitCost, string? Observation,
    int StockAvailableAtCreation, int MinimumStockAtCreation, int PendingOtherOrdersAtCreation,
    string NeedOrigin);

public sealed record PurchaseOrderDto(Guid Id, Guid SupplierId, string Status, string Prefix,
    string Series, long Consecutive, string VisibleNumber, long Version,
    DateTime CreatedAt, DateTime? SentAt, DateTime? ClosedAt,
    IReadOnlyCollection<PurchaseOrderLineDto> Lines, DateTime? CanceledAt,
    Guid? CloseReasonId, Guid? CancelReasonId, Guid? CloseAuthorizationId,
    Guid? CancelAuthorizationId);

public sealed record PurchaseOrderLineSummaryDto(string ProductCode, string ProductName,
    int RequestedQuantity, int ReceivedQuantity);

public sealed record PurchaseOrderSummaryDto(Guid Id, Guid SupplierId, string Status,
    string VisibleNumber, DateTime CreatedAt, long Version,
    IReadOnlyCollection<PurchaseOrderLineSummaryDto> Lines);

public sealed record PurchaseOrderNumberingDto(string Prefix, string Series,
    long LastNumber, long NextNumber, long Version);

public interface IPurchaseOrderService
{
    Task<Result<PagedResult<PurchaseOrderSummaryDto>>> ListAsync(
        ActorContext actor, PurchaseOrderListQuery query, CancellationToken ct = default);
    Task<Result<PurchaseOrderDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default);
    Task<Result<PurchaseOrderDto>> CreateAsync(
        ActorContext actor, CreatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<Result<PurchaseOrderDto>> UpdateAsync(
        ActorContext actor, Guid id, UpdatePurchaseOrderRequest request, CancellationToken ct = default);
    Task<Result<PurchaseOrderDto>> SendAsync(
        ActorContext actor, Guid id, SendPurchaseOrderRequest request, CancellationToken ct = default);
    Task<Result<PurchaseOrderDto>> CloseAsync(
        ActorContext actor, Guid id, ClosePurchaseOrderRequest request, CancellationToken ct = default);
    Task<Result<PurchaseOrderDto>> CancelAsync(
        ActorContext actor, Guid id, CancelPurchaseOrderRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyCollection<PurchaseOrderReasonDto>>> ListReasonsAsync(
        ActorContext actor, string kind, CancellationToken ct = default);
    Task<Result<IReadOnlyCollection<PurchaseOrderReasonDto>>> InitializeReasonsAsync(
        ActorContext actor, string kind, CancellationToken ct = default);
    Task<Result<PurchaseOrderNumberingDto>> GetNumberingAsync(
        ActorContext actor, CancellationToken ct = default);
    Task<Result<PurchaseOrderNumberingDto>> InitializeNumberingAsync(
        ActorContext actor, CancellationToken ct = default);
}
