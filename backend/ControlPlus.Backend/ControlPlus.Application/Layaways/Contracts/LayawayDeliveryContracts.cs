using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliverLayawayRequest(long ExpectedVersion);

public sealed record LayawayDeliveredProductDto(
    Guid ProductId, string ProductCode, string ProductName, string Unit, int Quantity);

public sealed record LayawayDeliveryDto(
    Guid LayawayId, string Number, string State, long Version,
    DateTimeOffset DeliveredAtUtc, Guid DeliveredByUserId, Guid CorrelationId,
    IReadOnlyCollection<LayawayDeliveredProductDto> Products);

public interface ILayawayDeliveryService
{
    Task<Result<LayawayDeliveryDto>> DeliverAsync(ActorContext actor, Guid layawayId,
        string idempotencyKey, DeliverLayawayRequest request,
        CancellationToken cancellationToken = default);
}
