using ControlPlus.Application.Backups;
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

    public Task<int> CountLowStockProductsAsync(CancellationToken cancellationToken) =>
        dbContext.Producto.AsNoTracking().CountAsync(LowStockSelection.Predicate, cancellationToken);

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

    public async Task<DashboardExternalBackup> GetExternalBackupAsync(Guid installationId, CancellationToken cancellationToken)
    {
        var destination = await dbContext.DestinoRespaldo.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.TipoDestino == BackupCodes.ExternalDestination && x.Activo)
            .Select(x => new { x.Id }).FirstOrDefaultAsync(cancellationToken);
        if (destination is null) return new DashboardExternalBackup(DashboardExternalBackupStates.NotConfigured, null, null);

        var last = await dbContext.CopiaRespaldo.AsNoTracking().Where(x => x.DestinoRespaldoId == destination.Id)
            .OrderByDescending(x => x.FechaUltimoIntento).ThenByDescending(x => x.Id)
            .Select(x => new { x.Estado, x.FechaUltimoIntento, x.FechaCompletada }).FirstOrDefaultAsync(cancellationToken);
        if (last is null) return new DashboardExternalBackup(DashboardExternalBackupStates.NoCopies, null, null);
        var lastSuccess = await dbContext.CopiaRespaldo.AsNoTracking()
            .Where(x => x.DestinoRespaldoId == destination.Id && x.Estado == BackupCodes.CopyCompleted && x.FechaCompletada != null)
            .MaxAsync(x => x.FechaCompletada, cancellationToken);
        var state = last.Estado switch
        {
            BackupCodes.CopyCompleted => DashboardExternalBackupStates.Succeeded,
            BackupCodes.CopyFailed => DashboardExternalBackupStates.Failed,
            _ => DashboardExternalBackupStates.Sending
        };
        return new DashboardExternalBackup(state, Utc(last.FechaUltimoIntento ?? last.FechaCompletada), Utc(lastSuccess));
    }

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
