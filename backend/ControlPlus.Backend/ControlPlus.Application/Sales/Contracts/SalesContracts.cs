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

public interface ISalesService
{
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
}
