using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Credits.Contracts;

public sealed record CreditInstallmentDto(Guid Id, int Number, decimal Amount, decimal Balance,
    DateOnly DueOn, DateOnly? PaidOn, string State);

public sealed record CreditDto(Guid Id, Guid SaleId, string SaleNumber, Guid CustomerId,
    string CustomerName, string? DocumentType, string? DocumentNumber, string? Phone,
    decimal OriginalAmount, decimal AdjustedAmount, decimal Balance, DateOnly GrantedOn,
    DateOnly DueOn, DateOnly? PaidOn, string State, long Version,
    IReadOnlyCollection<CreditInstallmentDto> Installments);

public sealed record CreditPaymentDto(Guid Id, string Number, string MethodCode, string MethodName,
    decimal AppliedAmount, decimal? ReceivedAmount, decimal Change, DateTimeOffset OccurredAtUtc,
    string State, Guid CorrelationId);

public sealed record CreditPaymentCancellationReasonDto(Guid Id, string Code, string Name, int DisplayOrder);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreditLifecycleAuthorizationRequest(
    string? UserName, string? Password, string? CredentialToken = null) : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelCreditPaymentRequest(
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] Guid ReasonId,
    string? Observation,
    CreditLifecycleAuthorizationRequest? Authorization = null);

public sealed record CancelCreditPaymentDto(Guid CreditId, decimal Balance, string State, long Version,
    IReadOnlyCollection<Guid> CancelledPaymentIds, Guid CorrelationId, DateTimeOffset CancelledAtUtc,
    decimal ReversedAmount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ForgiveCreditRequest(
    [property: JsonRequired] long ExpectedVersion,
    [property: JsonRequired] string Reason);

public sealed record ForgiveCreditDto(Guid CreditId, decimal Balance, string State, long Version,
    Guid AuthorizationId, Guid CorrelationId, DateTimeOffset ForgivenAtUtc);

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
    Task<Result<IReadOnlyCollection<CreditPaymentCancellationReasonDto>>> ListPaymentCancellationReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CancelCreditPaymentDto>> CancelPaymentAsync(ActorContext actor, Guid creditId, Guid paymentId,
        string idempotencyKey, CancelCreditPaymentRequest request, CancellationToken cancellationToken = default);
    Task<Result<ForgiveCreditDto>> ForgiveAsync(ActorContext actor, Guid creditId, string idempotencyKey,
        ForgiveCreditRequest request, CancellationToken cancellationToken = default);
}
