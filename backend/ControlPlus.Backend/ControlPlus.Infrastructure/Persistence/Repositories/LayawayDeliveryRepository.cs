using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfLayawayDeliveryRepository(OfficialControlPlusDbContext dbContext)
    : ILayawayDeliveryRepository
{
    public async Task<Apartado?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Apartado.FromSqlInterpolated(
            $"SELECT a.* FROM ventas.apartado AS a WHERE a.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    // Lines are historical snapshots; read them only after locking their header.
    public async Task<IReadOnlyCollection<DetalleApartado>> GetLinesAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.DetalleApartado.AsNoTracking().Where(x => x.ApartadoId == id && x.Vigente)
            .OrderBy(x => x.ProductoId).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
}
