using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LayawayPaymentCancellationAuthorization(string? UserName, string? Password,
    string? CredentialToken = null) : IAuthorizerCredentials
{
    [JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelLayawayPaymentRequest(long ExpectedVersion, Guid ReasonId,
    string? Observation, LayawayPaymentCancellationAuthorization? Authorization = null);

public sealed record LayawayPaymentCancellationReasonDto(Guid Id, string Code, string Name, int DisplayOrder);
public sealed record CancelLayawayPaymentDto(Guid LayawayId, decimal Balance, string State,
    long Version, decimal ReversedAmount, Guid CorrelationId, IReadOnlyCollection<Guid> CancelledPaymentIds);

public interface ILayawayPaymentCancellationService
{
    Task<Result<IReadOnlyCollection<LayawayPaymentCancellationReasonDto>>> ReasonsAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CancelLayawayPaymentDto>> CancelAsync(ActorContext actor, Guid layawayId,
        Guid paymentId, string idempotencyKey, CancelLayawayPaymentRequest request,
        CancellationToken cancellationToken = default);
}
