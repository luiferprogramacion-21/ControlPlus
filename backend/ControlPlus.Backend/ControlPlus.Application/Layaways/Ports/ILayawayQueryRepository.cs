using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public interface ILayawayQueryRepository
{
    Task<PagedResult<Apartado>> ListAsync(LayawayListQuery query, int offset,
        CancellationToken cancellationToken = default);

    Task<Apartado?> GetAsync(Guid layawayId, CancellationToken cancellationToken = default);
}
