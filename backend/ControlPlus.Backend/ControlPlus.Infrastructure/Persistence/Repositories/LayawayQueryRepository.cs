using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfLayawayQueryRepository(OfficialControlPlusDbContext dbContext)
    : ILayawayQueryRepository
{
    public async Task<PagedResult<Apartado>> ListAsync(LayawayListQuery query, int offset,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.Apartado.AsNoTracking().AsQueryable();
        if (query.CustomerId is Guid customerId) source = source.Where(x => x.ClienteId == customerId);
        if (query.DueFrom is DateOnly from) source = source.Where(x => x.FechaLimite >= from);
        if (query.DueTo is DateOnly to) source = source.Where(x => x.FechaLimite <= to);
        if (!string.IsNullOrWhiteSpace(query.State)) source = source.Where(x => x.Estado == query.State);

        var count = await source.CountAsync(cancellationToken);
        var rows = await source.Include(x => x.Cliente)
            .OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id)
            .Skip(offset).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Apartado>(rows, query.Page, query.PageSize, count);
    }

    public Task<Apartado?> GetAsync(Guid layawayId, CancellationToken cancellationToken = default) =>
        dbContext.Apartado.AsNoTracking().AsSplitQuery()
            .Include(x => x.Cliente)
            .Include(x => x.DetalleApartado)
            .Include(x => x.PagoApartado).ThenInclude(x => x.MetodoPago)
            .Include(x => x.PagoApartado).ThenInclude(x => x.MovimientoCaja)
            .Include(x => x.ReembolsoApartado).ThenInclude(x => x.MetodoPago)
            .Include(x => x.AutorizacionAnulacion)
            .Include(x => x.MovimientoInventario.Where(m => m.TipoMovimiento == "ENTREGA_APARTADO"))
            .SingleOrDefaultAsync(x => x.Id == layawayId, cancellationToken);
}
