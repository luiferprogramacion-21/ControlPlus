using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public interface ILayawayCreateRepository
{
    Task<Establecimiento?> GetEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken);
    Task<Cliente?> GetActiveCustomerForUpdateAsync(Guid customerId, CancellationToken cancellationToken);
    Task<SesionOperador?> GetActiveOperatorSessionForUpdateAsync(
        Guid shiftId, Guid terminalId, Guid userId, CancellationToken cancellationToken);
    Task<ConsecutivoDocumento?> GetSequenceForUpdateAsync(
        Guid installationId, string series, string documentType, CancellationToken cancellationToken);
    Task AddAsync(
        Apartado layaway, IReadOnlyCollection<DetalleApartado> lines,
        IReadOnlyCollection<PagoApartado> payments,
        IReadOnlyCollection<MovimientoInventario> inventoryMovements,
        CancellationToken cancellationToken);
}
