using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LayawayRefundRequest(string MethodCode, decimal Amount, string? Reference);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelLayawayRequest(long ExpectedVersion, decimal RefundAmount,
    IReadOnlyCollection<LayawayRefundRequest> Refunds, Guid? ReasonId,
    string? Observation, CreateLayawayAuthorizationRequest? Authorization = null);

public sealed record LayawayRefundDto(Guid Id, string MethodCode, decimal Amount,
    string? Reference, DateTimeOffset OccurredAtUtc);

public sealed record LayawayCancellationDto(Guid LayawayId, string Number, string State,
    long Version, decimal TotalPaid, decimal Refunded, decimal Retained,
    DateTimeOffset CancelledAtUtc, Guid CancelledByUserId, Guid AuthorizedByUserId,
    Guid? ReasonId, string? Observation, Guid CorrelationId,
    IReadOnlyCollection<LayawayRefundDto> Refunds);

public interface ILayawayCancellationService
{
    Task<Result<LayawayCancellationDto>> CancelAsync(ActorContext actor, Guid layawayId,
        string idempotencyKey, CancelLayawayRequest request,
        CancellationToken cancellationToken = default);
}
