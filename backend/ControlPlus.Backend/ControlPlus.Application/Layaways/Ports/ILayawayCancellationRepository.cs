using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public interface ILayawayCancellationRepository : ILayawayDeliveryRepository
{
    Task<decimal> GetTotalPaidAsync(Guid layawayId, CancellationToken cancellationToken);
    Task<MotivoOperacion?> GetReasonAsync(Guid establishmentId, Guid reasonId, CancellationToken cancellationToken);
    Task AddRefundsAsync(IReadOnlyCollection<ReembolsoApartado> refunds, CancellationToken cancellationToken);
}
