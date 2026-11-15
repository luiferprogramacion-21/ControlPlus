using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Reports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas de solo lectura del tablero. Las ventas usan la misma selección que el reporte financiero
/// para que «Ventas de hoy», la comparación y las barras por hora sumen siempre lo mismo.
/// </summary>
public sealed class EfDashboardOverviewRepository(OfficialControlPlusDbContext dbContext,
    ICashRepository cashRepository) : IDashboardOverviewRepository
{
    // El efectivo esperado sale del mismo cálculo del arqueo (apertura, cobros, abonos y movimientos manuales en
    // efectivo), de modo que el tablero y el cierre de caja muestran la misma cifra.
    public async Task<DashboardCash?> GetCurrentCashAsync(CancellationToken cancellationToken)
    {
        var shift = await cashRepository.GetOpenShiftAsync(false, cancellationToken);
        if (shift is null) return null;
        var expected = await cashRepository.GetExpectedAmountsAsync(shift.Id, cancellationToken);
        return new DashboardCash(
            new DateTimeOffset(DateTime.SpecifyKind(shift.FechaHoraApertura, DateTimeKind.Utc)),
            expected.Where(x => x.AffectsCash).Sum(x => x.ExpectedAmount));
    }

    public async Task<decimal> GetSalesTotalUntilAsync(Guid installationId, DateOnly operatingDate,
        DateTime untilUtc, CancellationToken cancellationToken) =>
        await dbContext.Venta.AsNoTracking()
            .Where(FinancialSalesReportSelection.For(installationId, operatingDate, operatingDate))
            .Where(x => x.FechaHora <= untilUtc)
            .SumAsync(x => x.Total, cancellationToken);

    public async Task<IReadOnlyList<DashboardSaleTime>> GetSaleTimesAsync(Guid installationId,
        DateOnly operatingDate, CancellationToken cancellationToken) =>
        await dbContext.Venta.AsNoTracking()
            .Where(FinancialSalesReportSelection.For(installationId, operatingDate, operatingDate))
            .Select(x => new DashboardSaleTime(x.FechaHora, x.Total))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DashboardSaleRow>> GetRecentSalesAsync(Guid installationId,
        DateOnly operatingDate, int limit, CancellationToken cancellationToken) =>
        await dbContext.Venta.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.FechaOperativa == operatingDate &&
                        (x.TipoVenta == "CONTADO" || x.TipoVenta == "CREDITO") &&
                        (x.Estado == "CONFIRMADA" || x.Estado == "ANULADA"))
            .OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new DashboardSaleRow(x.Id, x.Prefijo, x.Serie, x.Consecutivo, x.FechaHora,
                x.Cliente == null ? null : x.Cliente.NombreCompleto, x.Total, x.TipoVenta, x.Estado))
            .ToListAsync(cancellationToken);

    // Mismo criterio que la vista oficial compras.vw_necesidad_reposicion (agotado, o disponible por debajo del
    // mínimo, solo productos activos). Se consulta la tabla y no la vista para no depender de permisos extra.
    public Task<int> CountLowStockProductsAsync(CancellationToken cancellationToken) =>
        dbContext.Producto.AsNoTracking().CountAsync(x => x.Activo &&
            (x.StockActual - x.StockReservado == 0 || x.StockActual - x.StockReservado < x.StockMinimo),
            cancellationToken);

    public Task<int> CountOverdueCreditCustomersAsync(Guid installationId, DateOnly operatingDate,
        CancellationToken cancellationToken) =>
        dbContext.Credito.AsNoTracking()
            .Where(x => x.Venta.InstalacionId == installationId && x.Estado != "ANULADO" &&
                        x.SaldoPendiente > 0 && x.FechaVencimiento < operatingDate && x.Venta.ClienteId != null)
            .Select(x => x.Venta.ClienteId)
            .Distinct()
            .CountAsync(cancellationToken);

    public Task<int> CountUpcomingLayawaysAsync(Guid installationId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken) =>
        dbContext.Apartado.AsNoTracking().CountAsync(x => x.InstalacionId == installationId &&
            x.Estado == "ACTIVO" && x.FechaLimite >= from && x.FechaLimite <= to, cancellationToken);
}
