using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLayawayLineRequest(Guid ProductId, int Quantity, string PriceType);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLayawayPaymentRequest(
    string MethodCode, decimal AppliedAmount, decimal? ReceivedAmount, string? Reference);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLayawayAuthorizationRequest(string UserName, string Password);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLayawayRequest(
    Guid CustomerId,
    IReadOnlyCollection<CreateLayawayLineRequest> Lines,
    IReadOnlyCollection<CreateLayawayPaymentRequest> Payments,
    DateOnly? DueDate,
    CreateLayawayAuthorizationRequest Authorization);

public sealed record CreatedLayawayLineDto(
    Guid ProductId, string ProductCode, string ProductName, string MeasurementUnit,
    int Quantity, string PriceType, decimal UnitPrice, decimal Total);

public sealed record CreatedLayawayPaymentDto(
    string VisibleNumber, string MethodCode, decimal AppliedAmount,
    decimal? ReceivedAmount, decimal Change);

public sealed record CreatedLayawayDto(
    Guid Id, string VisibleNumber, Guid CustomerId, Guid ShiftId, Guid UserId,
    Guid? OperatorSessionId, DateOnly DueDate, decimal MinimumPercentage,
    decimal MinimumInitialPayment, decimal InitialPayment, decimal Total,
    decimal Balance, decimal Change, string State, long Version,
    IReadOnlyCollection<CreatedLayawayLineDto> Lines,
    IReadOnlyCollection<CreatedLayawayPaymentDto> Payments);

public interface ILayawayCreateService
{
    Task<Result<CreatedLayawayDto>> CreateAsync(
        ActorContext actor, string idempotencyKey, CreateLayawayRequest request,
        CancellationToken cancellationToken = default);
}
