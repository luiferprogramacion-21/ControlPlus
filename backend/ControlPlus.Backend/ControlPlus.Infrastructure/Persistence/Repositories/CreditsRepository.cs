using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Credits.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfCreditsRepository(OfficialControlPlusDbContext dbContext) : ICreditsRepository
{
    public async Task<PagedResult<Credito>> ListAsync(CreditListQuery query, DateOnly today, int offset,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.Credito.AsNoTracking().AsQueryable();
        if (query.CustomerId is Guid customerId) source = source.Where(x => x.Venta.ClienteId == customerId);
        if (query.DueFrom is DateOnly from) source = source.Where(x => x.FechaVencimiento >= from);
        if (query.DueTo is DateOnly to) source = source.Where(x => x.FechaVencimiento <= to);
        if (!string.IsNullOrWhiteSpace(query.State))
        {
            if (query.State == "VENCIDO")
                source = source.Where(x => x.Estado == "VENCIDO" ||
                    (x.Estado == "PENDIENTE" && x.SaldoPendiente > 0 && x.FechaVencimiento < today));
            else if (query.State == "PENDIENTE")
                source = source.Where(x => x.Estado == "PENDIENTE" &&
                    (x.SaldoPendiente <= 0 || x.FechaVencimiento >= today));
            else source = source.Where(x => x.Estado == query.State);
        }
        if (!string.IsNullOrWhiteSpace(query.SaleNumber))
        {
            var parts = query.SaleNumber.Trim().ToUpperInvariant().Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
                source = source.Where(x => x.Venta.Prefijo.StartsWith(parts[0]));
            else if (parts.Length == 2)
                source = source.Where(x => x.Venta.Prefijo == parts[0] && x.Venta.Serie.StartsWith(parts[1]));
            else if (parts.Length == 3 && parts[2].Length == 0)
                source = source.Where(x => x.Venta.Prefijo == parts[0] && x.Venta.Serie == parts[1]);
            else if (parts.Length == 3 && long.TryParse(parts[2], out var number) && number > 0)
                source = source.Where(x => x.Venta.Prefijo == parts[0] && x.Venta.Serie == parts[1] && x.Venta.Consecutivo == number);
            else
                source = source.Where(_ => false);
        }
        var count = await source.CountAsync(cancellationToken);
        var items = await source.Include(x => x.Venta).ThenInclude(x => x.Cliente)
            .OrderByDescending(x => x.FechaOtorgamiento).ThenByDescending(x => x.Id)
            .Skip(offset).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Credito>(items, query.Page, query.PageSize, count);
    }

    public Task<Credito?> GetAsync(Guid creditId, CancellationToken cancellationToken = default) =>
        dbContext.Credito.AsNoTracking().Include(x => x.Venta).ThenInclude(x => x.Cliente)
            .SingleOrDefaultAsync(x => x.Id == creditId, cancellationToken);

    public async Task<Credito?> GetForUpdateAsync(Guid creditId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Credito.FromSqlInterpolated(
            $"SELECT c.* FROM ventas.credito AS c WHERE c.id = {creditId} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<PagedResult<PagoCredito>> ListPaymentsAsync(
        Guid creditId, int page, int pageSize, int offset,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.PagoCredito.AsNoTracking().Where(x => x.CreditoId == creditId);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.Include(x => x.MetodoPago).Include(x => x.MovimientoCaja)
            .OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<PagoCredito>(items, page, pageSize, count);
    }

    public Task AddPaymentsAsync(IReadOnlyCollection<PagoCredito> payments, CancellationToken cancellationToken = default) =>
        dbContext.PagoCredito.AddRangeAsync(payments, cancellationToken);
}
