using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using System.Text.Json.Serialization;

namespace ControlPlus.Application.Cash.Contracts;

public sealed record CashRegisterDto(Guid Id, string Code, string Name, bool IsActive, long Version);

public sealed record CashClosureDifferenceReasonDto(Guid Id, string Code, string Name, bool IsActive);

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
    OperatorSessionDto? OwnOperatorSession,
    // Nombre de quien tiene abierto un turno individual, para explicar por qué otro usuario no puede vender
    // (decisión del 18 de noviembre de 2026). Solo el nombre: no revela montos ni identificadores.
    string? OpenShiftResponsibleName = null,
    // Nombre de quien pasó su carné en el turno compartido (20 de noviembre de 2026): la venta queda a su nombre.
    string? ActiveOperatorName = null,
    // Rol de quien pasó el carné (CAJERO, SUPERVISOR o ADMINISTRADOR): decide si el descuento o el mayorista piden autorización.
    string? ActiveOperatorRole = null);

public sealed record ConfigureCashRegisterRequest(string Code, string Name);

public sealed record UpdateCashShiftModeRequest(string Mode, long Version);

/// <summary>Second user (Supervisor or Administrador) who authorizes a Cajero to open the cash shift.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashOpenAuthorizationRequest(string? UserName, string? Password, string? CredentialToken = null)
    : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
/// <param name="Mode">INDIVIDUAL o COMPARTIDO para este turno; nulo usa el modo predeterminado del negocio (decisión del 18 de noviembre de 2026).</param>
public sealed record OpenCashShiftRequest(decimal InitialAmount, string? Observations, CashOpenAuthorizationRequest? Authorization = null, string? Mode = null);

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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectCashMovementRequest(long ExpectedShiftVersion, string Reason,
    string? ReplacementKind = null, decimal? ReplacementAmount = null, string? ReplacementConcept = null,
    Guid? OperatorSessionId = null, CashCloseAuthorizationRequest? Authorization = null);

public sealed record CashMovementCorrectionDto(Guid OriginalMovementId, Guid ReversalMovementId,
    Guid? ReplacementMovementId, decimal ExpectedCashAfter, Guid CorrelationId);

public sealed record CashClosureReviewDto(Guid Id, Guid ShiftId, Guid UserId, decimal ExpectedCash,
    decimal OriginalCount, decimal ReviewedCount, decimal ReviewedDifference, string Reason, DateTimeOffset OccurredAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewCashClosureRequest(long ExpectedShiftVersion, decimal ReviewedCount, string Reason);

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
public sealed record CashCloseAuthorizationRequest(string? UserName, string? Password, string? CredentialToken = null)
    : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CloseCashShiftRequest(
    long Version,
    IReadOnlyCollection<CountedPaymentMethodRequest> PaymentMethods,
    Guid? DifferenceReasonId,
    string? DifferenceObservation,
    CashCloseAuthorizationRequest? Authorization);

public sealed record IssueOperatorCredentialRequest(DateTimeOffset? ExpiresAtUtc);

public sealed record OperatorCredentialDto(
    Guid CredentialId, Guid UserId, string Format, string VisibleFragment,
    DateTimeOffset IssuedAtUtc, DateTimeOffset? ExpiresAtUtc, string State, long Version,
    string? CredentialToken);

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
    Task<Result<CashMovementCorrectionDto>> CorrectMovementAsync(ActorContext actor, Guid movementId, string key,
        CorrectCashMovementRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<Result<IReadOnlyCollection<CashShiftDto>>> ListClosedShiftsAsync(ActorContext actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<Result<IReadOnlyCollection<CashClosureReviewDto>>> ListClosureReviewsAsync(ActorContext actor, Guid shiftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<Result<CashClosureReviewDto>> ReviewClosureAsync(ActorContext actor, Guid shiftId, string key,
        ReviewCashClosureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<Result<IReadOnlyCollection<CashClosureDifferenceReasonDto>>> ListClosureDifferenceReasonsAsync(ActorContext actor, CancellationToken cancellationToken = default);
    /// <summary>Creates the default closing-difference reasons that are missing and returns the active ones.</summary>
    Task<Result<IReadOnlyCollection<CashClosureDifferenceReasonDto>>> InitializeClosureDifferenceReasonsAsync(ActorContext actor, CancellationToken cancellationToken = default);
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
    Task<Result<OperatorCredentialDto?>> GetOperatorCredentialAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);
    Task<Result> RevokeOperatorCredentialAsync(ActorContext actor, Guid userId, Guid credentialId, long expectedVersion, CancellationToken cancellationToken = default);
    Task<Result<OperatorSessionDto>> StartOperatorSessionAsync(ActorContext actor, StartOperatorSessionRequest request, CancellationToken cancellationToken = default);
    Task<Result<OperatorSessionDto>> CloseOperatorSessionAsync(ActorContext actor, Guid sessionId, CancellationToken cancellationToken = default);
}
