using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectLayawayProductRequest(Guid ProductId, int Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectLayawayProductsRequest(long ExpectedVersion, string Reason,
    IReadOnlyCollection<CorrectLayawayProductRequest> Lines, IReadOnlyCollection<LayawayRefundRequest> Refunds,
    LayawayPaymentCancellationAuthorization? Authorization = null);
public sealed record LayawayCorrectionDto(Guid Id, Guid LayawayId, long Version, decimal PreviousTotal,
    decimal Total, decimal PreviousBalance, decimal Balance, decimal Refunded, string Reason,
    DateTimeOffset OccurredAtUtc, Guid CorrelationId);
public interface ILayawayProductCorrectionService
{
    Task<Result<LayawayCorrectionDto>> CorrectAsync(ActorContext actor, Guid layawayId,
        string key, CorrectLayawayProductsRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<LayawayCorrectionDto>>> HistoryAsync(ActorContext actor, Guid layawayId,
        CancellationToken cancellationToken = default);
}
