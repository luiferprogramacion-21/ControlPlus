using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Purchases.Contracts;

public sealed record PurchaseListQuery(Guid? SupplierId = null, string? Status = null,
    DateOnly? InvoiceDate = null, string? SupplierDocumentNumber = null,
    int Page = 1, int PageSize = 20);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseLineRequest(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] int Quantity,
    [property: JsonRequired] decimal UnitCost,
    decimal? VatPercentage = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePurchaseRequest(
    [property: JsonRequired] Guid SupplierId,
    string? SupplierDocumentNumber,
    [property: JsonRequired] DateOnly InvoiceDate,
    [property: JsonRequired] string VatMode,
    [property: JsonRequired] IReadOnlyCollection<PurchaseLineRequest> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePurchaseRequest(
    [property: JsonRequired] Guid SupplierId,
    string? SupplierDocumentNumber,
    [property: JsonRequired] DateOnly InvoiceDate,
    [property: JsonRequired] string VatMode,
    [property: JsonRequired] IReadOnlyCollection<PurchaseLineRequest> Lines,
    [property: JsonRequired] long ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmPurchaseRequest([property: JsonRequired] long ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PurchaseReceiptLineRequest(
    [property: JsonRequired] Guid OrderLineId,
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] int Quantity,
    [property: JsonRequired] decimal UnitCost,
    decimal? VatPercentage = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceivePurchaseOrderRequest(
    [property: JsonRequired] long ExpectedOrderVersion,
    string? SupplierDocumentNumber,
    [property: JsonRequired] DateOnly InvoiceDate,
    [property: JsonRequired] string VatMode,
    [property: JsonRequired] IReadOnlyCollection<PurchaseReceiptLineRequest> Lines,
    CancelPurchaseAuthorizationRequest? Authorization = null);

public sealed record PurchaseReceiptDto(PurchaseDto Purchase, Guid OrderId, string OrderStatus,
    long OrderVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelPurchaseAuthorizationRequest(string? UserName, string? Password, string? CredentialToken = null)
    : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelPurchaseRequest(
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] Guid ReasonId,
    string? Observation,
    CancelPurchaseAuthorizationRequest? Authorization = null);

public sealed record PurchaseCancellationReasonDto(Guid Id, string Code, string Name, string? Description, int DisplayOrder);

public sealed record PurchaseLineDto(Guid Id, Guid ProductId, int Quantity, decimal UnitCost,
    string VatMode, decimal? VatPercentage, decimal GrossSubtotal, decimal? TaxBase,
    decimal? VatAmount, decimal Total, decimal? PreviousAverageCost, decimal ResultingAverageCost);

public sealed record PurchaseDto(Guid Id, Guid SupplierId, string? SupplierDocumentNumber,
    DateOnly InvoiceDate, string VatMode, string Status, string? Prefix, string? Series,
    long? Consecutive, string? VisibleNumber, decimal GrossSubtotal, decimal? VatTotal,
    decimal Total, long Version, IReadOnlyCollection<PurchaseLineDto> Lines, Guid? OrderId = null);

public sealed record PurchaseSummaryDto(Guid Id, Guid SupplierId, string? SupplierDocumentNumber,
    DateOnly InvoiceDate, string Status, string? VisibleNumber, decimal Total, long Version);

public interface IPurchaseService
{
    Task<Result<PagedResult<PurchaseSummaryDto>>> ListAsync(ActorContext actor, PurchaseListQuery query, CancellationToken ct = default);
    Task<Result<PurchaseDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default);
    Task<Result<PurchaseDto>> CreateAsync(ActorContext actor, CreatePurchaseRequest request, CancellationToken ct = default);
    Task<Result<PurchaseDto>> UpdateAsync(ActorContext actor, Guid id, UpdatePurchaseRequest request, CancellationToken ct = default);
    Task<Result<PurchaseDto>> ConfirmAsync(ActorContext actor, Guid id, string key, ConfirmPurchaseRequest request, CancellationToken ct = default);
    Task<Result<PurchaseReceiptDto>> ReceiveOrderAsync(ActorContext actor, Guid orderId, string key,
        ReceivePurchaseOrderRequest request, CancellationToken ct = default);
    Task<Result<PurchaseDto>> CancelAsync(ActorContext actor, Guid id, string key, CancelPurchaseRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyCollection<PurchaseCancellationReasonDto>>> ListCancellationReasonsAsync(ActorContext actor, CancellationToken ct = default);
    Task<Result<IReadOnlyCollection<PurchaseCancellationReasonDto>>> InitializeCancellationReasonsAsync(ActorContext actor, CancellationToken ct = default);
}
