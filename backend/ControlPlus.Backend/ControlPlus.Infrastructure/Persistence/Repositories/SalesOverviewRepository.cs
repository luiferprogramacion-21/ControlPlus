using ControlPlus.Application.Reports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfSalesOverviewRepository(OfficialControlPlusDbContext db) : ISalesOverviewRepository
{
    public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) =>
        db.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<SalesOverviewData> GetAsync(Guid installationId, DateOnly previousFrom,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        // All aggregates in a response must see the same state while sales are confirmed or voided.
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var sales = await db.Venta.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.FechaOperativa >= previousFrom &&
                x.FechaOperativa <= to && x.Estado == "CONFIRMADA" &&
                (x.TipoVenta == "CONTADO" || x.TipoVenta == "CREDITO"))
            .GroupBy(x => new { x.FechaOperativa, x.TipoVenta })
            .Select(g => new OverviewSaleRow(g.Key.FechaOperativa, g.Key.TipoVenta, g.LongCount(), g.Sum(x => x.Total)))
            .ToListAsync(ct);

        var changes = await db.CambioVenta.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.Estado == "CONFIRMADO" &&
                x.Venta.Estado == "CONFIRMADA" &&
                (x.Venta.TipoVenta == "CONTADO" || x.Venta.TipoVenta == "CREDITO") &&
                x.TurnoCaja.FechaOperativa >= from && x.TurnoCaja.FechaOperativa <= to)
            .GroupBy(x => x.TurnoCaja.FechaOperativa)
            .Select(g => new OverviewChangeRow(g.Key, g.LongCount(),
                g.Sum(x => x.TotalEntregado - x.TotalDevuelto)))
            .ToListAsync(ct);

        var includedVat = await db.DetalleVenta.AsNoTracking()
            .Where(x => x.Venta.InstalacionId == installationId && x.Venta.FechaOperativa >= from &&
                x.Venta.FechaOperativa <= to && x.Venta.Estado == "CONFIRMADA" &&
                (x.Venta.TipoVenta == "CONTADO" || x.Venta.TipoVenta == "CREDITO"))
            .SumAsync(x => x.Venta.IvaDiscriminadoCompleto ? (x.ValorIva ?? 0m) : 0m, ct);

        var includedChangeVat = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.CambioVenta.InstalacionId == installationId &&
                x.CambioVenta.Estado == "CONFIRMADO" && x.CambioVenta.Venta.Estado == "CONFIRMADA" &&
                (x.CambioVenta.Venta.TipoVenta == "CONTADO" || x.CambioVenta.Venta.TipoVenta == "CREDITO") &&
                x.CambioVenta.TurnoCaja.FechaOperativa >= from && x.CambioVenta.TurnoCaja.FechaOperativa <= to &&
                x.CambioVenta.Venta.IvaDiscriminadoCompleto)
            .SumAsync(x => x.TipoDetalle == "ENTREGADO" ? (x.ValorIva ?? 0m) : -(x.ValorIva ?? 0m), ct);

        var originalCost = await db.DetalleVenta.AsNoTracking()
            .Where(x => x.Venta.InstalacionId == installationId && x.Venta.FechaOperativa >= from &&
                x.Venta.FechaOperativa <= to && x.Venta.Estado == "CONFIRMADA" &&
                (x.Venta.TipoVenta == "CONTADO" || x.Venta.TipoVenta == "CREDITO"))
            .GroupBy(_ => 1)
            .Select(g => new OverviewCostRow(
                g.Sum(x => x.CostoUnitario == null ? 0m : x.CostoUnitario.Value * x.Cantidad),
                g.LongCount(x => x.CostoUnitario == null), 0))
            .SingleOrDefaultAsync(ct) ?? new OverviewCostRow(0, 0, 0);

        var returnedCost = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.TipoDetalle == "DEVUELTO" && x.CambioVenta.InstalacionId == installationId &&
                x.CambioVenta.Estado == "CONFIRMADO" && x.CambioVenta.Venta.Estado == "CONFIRMADA" &&
                (x.CambioVenta.Venta.TipoVenta == "CONTADO" || x.CambioVenta.Venta.TipoVenta == "CREDITO") &&
                x.CambioVenta.TurnoCaja.FechaOperativa >= from && x.CambioVenta.TurnoCaja.FechaOperativa <= to)
            .GroupBy(_ => 1)
            .Select(g => new OverviewCostRow(
                g.Sum(x => x.DetalleVentaOrigen == null || x.DetalleVentaOrigen.CostoUnitario == null
                    ? 0m : x.DetalleVentaOrigen.CostoUnitario.Value * x.Cantidad),
                g.LongCount(x => x.DetalleVentaOrigen == null || x.DetalleVentaOrigen.CostoUnitario == null), 0))
            .SingleOrDefaultAsync(ct) ?? new OverviewCostRow(0, 0, 0);

        // One delivered line per product/change is enforced by the sale-change use case.
        // Project only the data needed to reconcile against historical inventory movements.
        var delivered = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.TipoDetalle == "ENTREGADO" && x.CambioVenta.InstalacionId == installationId &&
                x.CambioVenta.Estado == "CONFIRMADO" && x.CambioVenta.Venta.Estado == "CONFIRMADA" &&
                (x.CambioVenta.Venta.TipoVenta == "CONTADO" || x.CambioVenta.Venta.TipoVenta == "CREDITO") &&
                x.CambioVenta.TurnoCaja.FechaOperativa >= from && x.CambioVenta.TurnoCaja.FechaOperativa <= to)
            .Select(x => new { x.CambioVentaId, x.ProductoId, x.Cantidad })
            .ToListAsync(ct);
        var movements = await db.MovimientoInventario.AsNoTracking()
            .Where(x => x.CambioVentaId != null && x.TipoMovimiento == "CAMBIO_ENTREGA" &&
                x.CambioVenta!.InstalacionId == installationId && x.CambioVenta.Estado == "CONFIRMADO" &&
                x.CambioVenta.Venta.Estado == "CONFIRMADA" &&
                (x.CambioVenta.Venta.TipoVenta == "CONTADO" || x.CambioVenta.Venta.TipoVenta == "CREDITO") &&
                x.CambioVenta.TurnoCaja.FechaOperativa >= from && x.CambioVenta.TurnoCaja.FechaOperativa <= to)
            .Select(x => new { x.CambioVentaId, x.ProductoId, x.CantidadStock, x.CostoPromedioAnterior })
            .ToListAsync(ct);
        var movementLookup = movements.ToLookup(x => (x.CambioVentaId, x.ProductoId));
        decimal deliveredKnownCost = 0;
        long missingCostLines = 0, missingMovementLines = 0;
        foreach (var line in delivered)
        {
            var matches = movementLookup[(line.CambioVentaId, line.ProductoId)].ToArray();
            if (matches.Length != 1 || matches[0].CantidadStock != -line.Cantidad)
                missingMovementLines++;
            else if (matches[0].CostoPromedioAnterior is decimal cost)
                deliveredKnownCost += cost * line.Cantidad;
            else
                missingCostLines++;
        }
        var deliveredCost = new OverviewCostRow(deliveredKnownCost, missingCostLines, missingMovementLines);

        var payments = await db.PagoVenta.AsNoTracking()
            .Where(x => x.Estado == "APLICADO" && x.Venta.InstalacionId == installationId &&
                x.Venta.FechaOperativa >= from && x.Venta.FechaOperativa <= to &&
                x.Venta.Estado == "CONFIRMADA" && x.Venta.TipoVenta == "CONTADO")
            .GroupBy(x => new { x.MetodoPago.Codigo, x.MetodoPago.Nombre })
            .Select(g => new OverviewPaymentRow(g.Key.Codigo, g.Key.Nombre, g.Sum(x => x.ValorAplicado)))
            .ToListAsync(ct);
        await snapshot.CommitAsync(ct);
        return new SalesOverviewData(sales, changes, originalCost, returnedCost, deliveredCost,
            payments, includedVat, includedChangeVat);
    }
}
