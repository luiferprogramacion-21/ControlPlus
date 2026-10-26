using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using System.Text.Json.Serialization;

namespace ControlPlus.Application.Sales.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleDiscountRequest(string Type, decimal Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleLineRequest(
    Guid ProductId,
    int Quantity,
    string PriceType,
    SaleDiscountRequest? Discount = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalePaymentRequest(
    string MethodCode,
    decimal AppliedAmount,
    decimal? ReceivedAmount,
    string? Reference);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmSaleRequest(
    Guid? CustomerId,
    IReadOnlyCollection<SaleLineRequest> Lines,
    SaleDiscountRequest? GeneralDiscount,
    string? DiscountReason,
    IReadOnlyCollection<SalePaymentRequest> Payments,
    string? SaleType = null,
    DateOnly? DueDate = null,
    SaleCreditAuthorizationRequest? Authorization = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleCreditAuthorizationRequest(string UserName, string Password);

public sealed record SaleLineDto(
    Guid Id,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string MeasurementUnit,
    int Quantity,
    string PriceType,
    decimal UnitPrice,
    string DiscountOrigin,
    string? DiscountType,
    decimal? DiscountPercentage,
    decimal DiscountAmount,
    decimal GrossAmount,
    decimal NetAmount);

public sealed record SalePaymentDto(
    Guid Id,
    string MethodCode,
    string MethodName,
    decimal AppliedAmount,
    decimal? ReceivedAmount,
    decimal Change,
    string? Reference);

public sealed record SaleDto(
    Guid Id,
    string VisibleNumber,
    Guid ShiftId,
    Guid UserId,
    Guid? OperatorSessionId,
    Guid? CustomerId,
    DateTimeOffset OccurredAtUtc,
    DateOnly OperatingDate,
    DateOnly ChangeDeadline,
    string DiscountScope,
    string? DiscountType,
    decimal? RequestedDiscountValue,
    string? DiscountReason,
    decimal GrossSubtotal,
    decimal DiscountTotal,
    decimal Total,
    decimal Change,
    string State,
    long Version,
    IReadOnlyCollection<SaleLineDto> Lines,
    IReadOnlyCollection<SalePaymentDto> Payments,
    Guid? CreditId = null);

public sealed record SaleSummaryDto(
    Guid Id,
    string VisibleNumber,
    Guid UserId,
    Guid? CustomerId,
    DateTimeOffset OccurredAtUtc,
    DateOnly OperatingDate,
    decimal Total,
    string State);

public sealed record SaleListQuery(int Page = 1, int PageSize = 20);

public sealed record SaleChangeLineAvailabilityDto(Guid SaleLineId, Guid ProductId,
    string ProductCode, string ProductName, int SoldQuantity, int ReturnedQuantity,
    int RemainingQuantity);

public sealed record SaleChangePreparationDto(Guid SaleId, string VisibleNumber, string SaleType,
    string SaleState, long SaleVersion, DateOnly OperatingDate, DateOnly ChangeDeadline,
    DateOnly CurrentLocalDate, bool WithinDeadline, bool CanStartChange,
    IReadOnlyCollection<SaleChangeLineAvailabilityDto> Lines,
    Guid? CreditId, long? CreditVersion, decimal? CreditBalance);

public sealed record SaleChangePolicyDto(int Days, long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSaleChangePolicyRequest([property: JsonRequired] int Days,
    [property: JsonRequired] long ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleChangeReturnRequest(Guid SaleLineId, int Quantity, bool Resellable);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleChangeDeliveryRequest(Guid ProductId, int Quantity, string PriceType);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleChangePaymentRequest(string MethodCode, decimal Amount,
    decimal? ReceivedAmount, string? Reference);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmSaleChangeRequest(
    [property: JsonRequired] long ExpectedSaleVersion,
    long? ExpectedCreditVersion,
    IReadOnlyCollection<SaleChangeReturnRequest> Returned,
    IReadOnlyCollection<SaleChangeDeliveryRequest> Delivered,
    string Treatment,
    IReadOnlyCollection<SaleChangePaymentRequest> Payments,
    SaleCreditAuthorizationRequest? Authorization,
    string? Reason = null);

public sealed record SaleChangeDto(Guid Id, Guid SaleId, string VisibleNumber,
    string State, DateTimeOffset OccurredAtUtc, decimal TotalReturned,
    decimal TotalDelivered, decimal Difference, string Treatment,
    decimal CreditAdjustment, decimal PaidOrRefunded, Guid CorrelationId,
    IReadOnlyCollection<SaleChangeDetailDto> Details,
    IReadOnlyCollection<SaleChangePaymentDto> Payments);

public sealed record SaleChangeDetailDto(Guid Id, string Type, Guid ProductId,
    Guid? OriginalSaleLineId, int Quantity, decimal UnitPrice,
    decimal Discount, decimal NetAmount);

public sealed record SaleChangePaymentDto(Guid Id, string Type, string MethodCode,
    decimal Amount, decimal? ReceivedAmount, decimal Change, string? Reference);

public sealed record SaleReceiptDto(Guid SaleId, string VisibleNumber, DateTimeOffset OccurredAtUtc,
    DateOnly OperatingDate, string SaleType, string State, decimal GrossSubtotal,
    decimal DiscountTotal, decimal Total, decimal Change,
    IReadOnlyCollection<SaleLineDto> Lines, IReadOnlyCollection<SaleReceiptPaymentDto> Payments);

public sealed record SaleReceiptPaymentDto(string MethodCode, decimal AppliedAmount,
    decimal? ReceivedAmount, decimal Change);

public sealed record SaleReprintRequestDto(Guid RequestId, Guid SaleId, DateTimeOffset RequestedAtUtc,
    string Status);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelSaleRequest([property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] Guid ReasonId, string? Observation,
    SaleCreditAuthorizationRequest? Authorization = null);

public sealed record SaleCancellationReasonDto(Guid Id, string Code, string Name, int DisplayOrder);

public sealed record CancelSaleDto(Guid SaleId, string VisibleNumber, string State, long Version,
    Guid CancellationId, Guid CorrelationId, DateTimeOffset CancelledAtUtc,
    string SaleType, decimal ReversedAmount);

public interface ISalesService
{
    Task<Result<SaleChangePolicyDto>> GetChangePolicyAsync(ActorContext actor,
        CancellationToken cancellationToken = default);
    Task<Result<SaleChangePolicyDto>> UpdateChangePolicyAsync(ActorContext actor,
        UpdateSaleChangePolicyRequest request, CancellationToken cancellationToken = default);
    Task<Result<SaleChangePreparationDto>> FindChangeSaleAsync(ActorContext actor,
        string visibleNumber, CancellationToken cancellationToken = default);
    Task<Result<SaleChangePreparationDto>> GetChangePreparationAsync(ActorContext actor,
        Guid saleId, CancellationToken cancellationToken = default);
    Task<Result<SaleChangeDto>> ConfirmChangeAsync(ActorContext actor, Guid saleId,
        string idempotencyKey, ConfirmSaleChangeRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<SaleChangeDto>>> ListChangesAsync(ActorContext actor,
        Guid saleId, CancellationToken cancellationToken = default);
    Task<Result<SaleDto>> ConfirmAsync(
        ActorContext actor,
        string idempotencyKey,
        ConfirmSaleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SaleSummaryDto>>> ListOwnAsync(
        ActorContext actor,
        SaleListQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SaleSummaryDto>>> ListAllAsync(
        ActorContext actor,
        SaleListQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<SaleDto>> GetAsync(
        ActorContext actor,
        Guid saleId,
        CancellationToken cancellationToken = default);

    Task<Result<SaleReceiptDto>> GetReceiptAsync(ActorContext actor, Guid saleId,
        CancellationToken cancellationToken = default);

    Task<Result<SaleReprintRequestDto>> RequestReprintAsync(ActorContext actor, Guid saleId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<SaleCancellationReasonDto>>> ListCancellationReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<SaleCancellationReasonDto>>> InitializeCancellationReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CancelSaleDto>> CancelAsync(ActorContext actor, Guid saleId, string idempotencyKey,
        CancelSaleRequest request, CancellationToken cancellationToken = default);
}
