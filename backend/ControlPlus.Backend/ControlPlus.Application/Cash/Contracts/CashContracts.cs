using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using System.Text.Json.Serialization;

namespace ControlPlus.Application.Cash.Contracts;

public sealed record CashRegisterDto(Guid Id, string Code, string Name, bool IsActive, long Version);

public sealed record CashShiftDto(
    Guid Id,
    Guid CashRegisterId,
    Guid TerminalId,
    string Mode,
    Guid OpenedByUserId,
    Guid? ResponsibleUserId,
    Guid? ClosedByUserId,
    DateOnly OperatingDate,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    decimal InitialAmount,
    decimal? CashExpected,
    decimal? CashCounted,
    decimal? CashDifference,
    decimal? TotalDifference,
    string State,
    string? Observations,
    string? DifferenceObservation,
    Guid? DifferenceReasonId,
    Guid? CloseAuthorizationId,
    long Version);

public sealed record CashStateDto(
    CashRegisterDto? CashRegister,
    CashShiftDto? CurrentShift,
    string? DefaultShiftMode,
    long? ConfigurationVersion,
    bool RequiresPreviousShiftClosure,
    string? Guidance,
    bool IsConfigured,
    bool HasOpenShift,
    bool RequiresOperatorSession,
    OperatorSessionDto? OwnOperatorSession);

public sealed record ConfigureCashRegisterRequest(string Code, string Name);

public sealed record UpdateCashShiftModeRequest(string Mode, long Version);

public sealed record OpenCashShiftRequest(decimal InitialAmount, string? Observations);

public sealed record CashMovementDto(
    Guid Id,
    Guid ShiftId,
    Guid UserId,
    Guid? OperatorSessionId,
    string Type,
    string Category,
    decimal Amount,
    string? Concept,
    DateTimeOffset OccurredAtUtc,
    Guid CorrelationId);

public sealed record CashMovementListQuery(int Page = 1, int PageSize = 50);

public sealed record CreateCashMovementRequest(decimal Amount, string? Concept, Guid? OperatorSessionId);

public sealed record PaymentMethodReconciliationDto(
    Guid PaymentMethodId,
    string Code,
    string Name,
    bool AffectsCash,
    decimal ExpectedAmount,
    decimal? CountedAmount,
    decimal? Difference);

public sealed record CashReconciliationDto(
    Guid ShiftId,
    long ShiftVersion,
    decimal TotalExpected,
    decimal? TotalCounted,
    decimal? TotalDifference,
    IReadOnlyCollection<PaymentMethodReconciliationDto> PaymentMethods,
    decimal? CashDifference);

public sealed record CountedPaymentMethodRequest(Guid PaymentMethodId, decimal CountedAmount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashCloseAuthorizationRequest(string UserName, string Password);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CloseCashShiftRequest(
    long Version,
    IReadOnlyCollection<CountedPaymentMethodRequest> PaymentMethods,
    Guid? DifferenceReasonId,
    string? DifferenceObservation,
    CashCloseAuthorizationRequest? Authorization);

public sealed record IssueOperatorCredentialRequest(DateTimeOffset? ExpiresAtUtc);

public sealed record IssuedOperatorCredentialDto(
    Guid CredentialId,
    Guid UserId,
    string Format,
    string CredentialToken,
    string VisibleFragment,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

public sealed record StartOperatorSessionRequest(string CredentialToken);

public sealed record OperatorSessionDto(
    Guid Id,
    Guid ShiftId,
    Guid TerminalId,
    Guid UserId,
    Guid CredentialId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastUsedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string State,
    string? CloseReason,
    Guid CorrelationId);

public sealed record ExpectedPaymentMethodAmount(
    Guid PaymentMethodId,
    string Code,
    string Name,
    bool AffectsCash,
    decimal ExpectedAmount);

public interface ICashService
{
    Task<Result<CashStateDto>> GetStateAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CashRegisterDto>> ConfigureCashRegisterAsync(ActorContext actor, ConfigureCashRegisterRequest request, CancellationToken cancellationToken = default);
    Task<Result<CashStateDto>> UpdateShiftModeAsync(ActorContext actor, UpdateCashShiftModeRequest request, CancellationToken cancellationToken = default);
    Task<Result<CashShiftDto>> OpenShiftAsync(ActorContext actor, OpenCashShiftRequest request, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<CashMovementDto>>> ListMovementsAsync(ActorContext actor, CashMovementListQuery query, CancellationToken cancellationToken = default);
    Task<Result<CashMovementDto>> RegisterIncomeAsync(ActorContext actor, CreateCashMovementRequest request, CancellationToken cancellationToken = default);
    Task<Result<CashMovementDto>> RegisterExpenseAsync(ActorContext actor, CreateCashMovementRequest request, CancellationToken cancellationToken = default);
    Task<Result<CashMovementDto>> RegisterCashDropAsync(ActorContext actor, CreateCashMovementRequest request, CancellationToken cancellationToken = default);
    Task<Result<CashReconciliationDto>> GetReconciliationAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CashReconciliationDto>> CloseShiftAsync(ActorContext actor, Guid shiftId, CloseCashShiftRequest request, CancellationToken cancellationToken = default);
    Task<Result<IssuedOperatorCredentialDto>> IssueOperatorCredentialAsync(ActorContext actor, Guid userId, IssueOperatorCredentialRequest request, CancellationToken cancellationToken = default);
    Task<Result<OperatorSessionDto>> StartOperatorSessionAsync(ActorContext actor, StartOperatorSessionRequest request, CancellationToken cancellationToken = default);
    Task<Result<OperatorSessionDto>> CloseOperatorSessionAsync(ActorContext actor, Guid sessionId, CancellationToken cancellationToken = default);
}
