using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplyLayawayPaymentRequest(long ExpectedVersion,
    IReadOnlyCollection<CreateLayawayPaymentRequest> Payments);

public sealed record LayawayPaymentOperationDto(Guid LayawayId, decimal Balance, string State,
    long Version, Guid CorrelationId, decimal Change,
    IReadOnlyCollection<CreatedLayawayPaymentDto> Payments);

public interface ILayawayPaymentService
{
    Task<Result<LayawayPaymentOperationDto>> ApplyAsync(ActorContext actor, Guid layawayId,
        string idempotencyKey, ApplyLayawayPaymentRequest request,
        CancellationToken cancellationToken = default);
}
