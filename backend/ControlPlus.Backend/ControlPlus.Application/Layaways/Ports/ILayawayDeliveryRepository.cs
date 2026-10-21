using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public interface ILayawayDeliveryRepository
{
    Task<Apartado?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DetalleApartado>> GetLinesAsync(
        Guid id, CancellationToken cancellationToken = default);
}
