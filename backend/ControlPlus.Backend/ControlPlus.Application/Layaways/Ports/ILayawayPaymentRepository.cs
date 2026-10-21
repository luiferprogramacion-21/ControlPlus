using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public interface ILayawayPaymentRepository
{
    Task<Apartado?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task AddPaymentsAsync(IReadOnlyCollection<PagoApartado> payments, CancellationToken cancellationToken);
}
