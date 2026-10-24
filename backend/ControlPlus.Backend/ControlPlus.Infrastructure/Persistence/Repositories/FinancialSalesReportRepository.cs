using ControlPlus.Application.Reports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfFinancialSalesReportRepository(OfficialControlPlusDbContext dbContext) : IFinancialSalesReportRepository
{
    public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) =>
        dbContext.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesReportRow>> GetDailySalesAsync(
        Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await dbContext.Venta.AsNoTracking()
            .Where(FinancialSalesReportSelection.For(installationId, from, to))
            .GroupBy(x => new { x.FechaOperativa, x.TipoVenta })
            .Select(g => new SalesReportRow(g.Key.FechaOperativa, g.Key.TipoVenta, g.LongCount(), g.Sum(x => x.Total)))
            .ToListAsync(cancellationToken);
}

public static class FinancialSalesReportSelection
{
    public static Expression<Func<Venta, bool>> For(Guid installationId, DateOnly from, DateOnly to) =>
        x => x.InstalacionId == installationId && x.FechaOperativa >= from && x.FechaOperativa <= to &&
             x.Estado == "CONFIRMADA" && (x.TipoVenta == "CONTADO" || x.TipoVenta == "CREDITO");
}
