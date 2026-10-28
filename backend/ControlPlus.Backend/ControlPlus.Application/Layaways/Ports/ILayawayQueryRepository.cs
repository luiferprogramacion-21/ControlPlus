using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public sealed record LayawayQueryInstallation(Guid Id, string TimeZoneId);

public interface ILayawayQueryRepository
{
    Task<LayawayQueryInstallation?> GetActiveInstallationAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<Apartado>> ListAsync(LayawayListQuery query, int offset, Guid installationId, DateOnly operatingDate,
        CancellationToken cancellationToken = default);

    Task<Apartado?> GetAsync(Guid layawayId, Guid installationId, CancellationToken cancellationToken = default);
}
