using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Credits.Contracts;

public sealed record CreditDto(Guid Id, Guid SaleId, string SaleNumber, Guid CustomerId,
    string CustomerName, string? DocumentType, string? DocumentNumber, string? Phone,
    decimal OriginalAmount, decimal AdjustedAmount, decimal Balance, DateOnly GrantedOn,
    DateOnly DueOn, DateOnly? PaidOn, string State, long Version);

public sealed record CreditPaymentDto(Guid Id, string Number, string MethodCode, string MethodName,
    decimal AppliedAmount, decimal? ReceivedAmount, decimal Change, DateTimeOffset OccurredAtUtc,
    string State, Guid CorrelationId);

public sealed record CreditPaymentOperationDto(Guid CreditId, decimal Balance, string State, long Version,
    Guid CorrelationId, IReadOnlyCollection<CreditPaymentDto> Payments);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreditPaymentMethodRequest(string MethodCode, decimal AppliedAmount,
    decimal? ReceivedAmount, string? Reference);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplyCreditPaymentRequest(long ExpectedVersion,
    IReadOnlyCollection<CreditPaymentMethodRequest> Payments);

public sealed record CreditListQuery(int Page = 1, int PageSize = 20, string? State = null,
    DateOnly? DueFrom = null, DateOnly? DueTo = null, Guid? CustomerId = null,
    string? SaleNumber = null);

public interface ICreditsService
{
    Task<Result<PagedResult<CreditDto>>> ListAsync(ActorContext actor, CreditListQuery query,
        CancellationToken cancellationToken = default);
    Task<Result<CreditDto>> GetAsync(ActorContext actor, Guid creditId,
        CancellationToken cancellationToken = default);
    Task<Result<PagedResult<CreditPaymentDto>>> ListPaymentsAsync(ActorContext actor, Guid creditId,
        int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<CreditPaymentOperationDto>> ApplyPaymentAsync(ActorContext actor, Guid creditId,
        string idempotencyKey, ApplyCreditPaymentRequest request, CancellationToken cancellationToken = default);
}
