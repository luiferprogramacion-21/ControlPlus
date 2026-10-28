using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

public sealed record LayawayListQuery(int Page = 1, int PageSize = 20, string? State = null,
    Guid? CustomerId = null, DateOnly? DueFrom = null, DateOnly? DueTo = null, bool? Overdue = null);

public sealed record LayawayQueryDto(Guid Id, string Number, Guid CustomerId, string CustomerName,
    DateTimeOffset CreatedAtUtc, DateOnly DueOn, decimal Total, decimal InitialPayment,
    decimal Balance, string State, long Version, bool IsOverdue);

public sealed record LayawayLineDto(Guid ProductId, string ProductCode, string ProductName,
    string Unit, int Quantity, decimal UnitPrice, decimal Subtotal);

public sealed record LayawayPaymentDto(Guid Id, string Number, string MethodCode, string MethodName,
    decimal AppliedAmount, decimal? ReceivedAmount, decimal Change,
    DateTimeOffset OccurredAtUtc, string State, Guid? CorrelationId);

public sealed record LayawayDetailDto(LayawayQueryDto Layaway, decimal InitialMinimumPercentage,
    IReadOnlyCollection<LayawayLineDto> Lines, IReadOnlyCollection<LayawayPaymentDto> Payments,
    LayawayDeliveryDto? Delivery, LayawayCancellationDto? Cancellation = null);

public interface ILayawayQueryService
{
    Task<Result<PagedResult<LayawayQueryDto>>> ListAsync(ActorContext actor, LayawayListQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<LayawayDetailDto>> GetAsync(ActorContext actor, Guid layawayId,
        CancellationToken cancellationToken = default);
}
