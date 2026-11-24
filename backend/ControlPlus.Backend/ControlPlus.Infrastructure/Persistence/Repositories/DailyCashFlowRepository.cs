using ControlPlus.Application.Reports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfDailyCashFlowRepository(OfficialControlPlusDbContext dbContext) : IDailyCashFlowRepository
{
    public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) =>
        dbContext.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CashFlowEntry>> GetEntriesAsync(Guid installationId, DateOnly from,
        DateOnly to, CancellationToken cancellationToken)
    {
        var shifts = await dbContext.TurnoCaja.AsNoTracking()
            .Where(x => x.Caja.InstalacionId == installationId && x.FechaOperativa >= from && x.FechaOperativa <= to)
            .Select(x => new { x.Id, x.FechaOperativa }).ToArrayAsync(cancellationToken);
        if (shifts.Length == 0) return [];
        var shiftIds = shifts.Select(x => x.Id).ToArray();
        var dates = shifts.ToDictionary(x => x.Id, x => x.FechaOperativa);
        var current = await dbContext.MovimientoCaja.AsNoTracking()
            .Where(x => shiftIds.Contains(x.TurnoCajaId))
            .Select(x => new MovementRow(x.Id, x.TurnoCajaId, x.Tipo, x.CategoriaMovimiento,
                x.Valor, x.MovimientoRevertidoId)).ToArrayAsync(cancellationToken);
        var reversedIds = current.Where(x => x.ReversedId.HasValue).Select(x => x.ReversedId!.Value).Distinct().ToArray();
        var originalMovements = await dbContext.MovimientoCaja.AsNoTracking()
            .Where(x => reversedIds.Contains(x.Id) && x.TurnoCaja.Caja.InstalacionId == installationId)
            .Select(x => new MovementRow(x.Id, x.TurnoCajaId, x.Tipo, x.CategoriaMovimiento,
                x.Valor, x.MovimientoRevertidoId)).ToArrayAsync(cancellationToken);
        var allMovements = current.Concat(originalMovements).DistinctBy(x => x.Id).ToDictionary(x => x.Id);
        var movementIds = allMovements.Keys.ToArray();
        var payments = new List<PaymentRow>();

        // Each sale payment belongs to the shift of its cash movement (D3: a closed shift's correction lives in today's shift).
        payments.AddRange(await dbContext.PagoVenta.AsNoTracking()
            .Where(x => x.Venta.InstalacionId == installationId &&
                (shiftIds.Contains(x.MovimientoCaja != null ? x.MovimientoCaja.TurnoCajaId : x.Venta.TurnoCajaId) ||
                 (x.MovimientoCajaId.HasValue && movementIds.Contains(x.MovimientoCajaId.Value))))
            .Select(x => new PaymentRow(x.MovimientoCaja != null ? x.MovimientoCaja.TurnoCajaId : x.Venta.TurnoCajaId,
                x.MovimientoCajaId, "VENTA_CONTADO",
                x.MetodoPago.Codigo, x.ValorAplicado, x.Estado,
                x.MovimientoCaja != null && x.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.PagoCredito.AsNoTracking()
            .Where(x => x.InstalacionId == installationId &&
                (shiftIds.Contains(x.TurnoCajaId) ||
                 (x.MovimientoCajaId.HasValue && movementIds.Contains(x.MovimientoCajaId.Value))))
            .Select(x => new PaymentRow(x.TurnoCajaId, x.MovimientoCajaId, "ABONO_CREDITO",
                x.MetodoPago.Codigo, x.ValorAplicado, x.Estado,
                x.MovimientoCaja != null && x.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.PagoApartado.AsNoTracking()
            .Where(x => x.InstalacionId == installationId &&
                (shiftIds.Contains(x.TurnoCajaId) ||
                 (x.MovimientoCajaId.HasValue && movementIds.Contains(x.MovimientoCajaId.Value))))
            .Select(x => new PaymentRow(x.TurnoCajaId, x.MovimientoCajaId, "PAGO_APARTADO",
                x.MetodoPago.Codigo, x.ValorAplicado, x.Estado,
                x.MovimientoCaja != null && x.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.PagoCambioVenta.AsNoTracking()
            .Where(x => x.TurnoCaja.Caja.InstalacionId == installationId &&
                (shiftIds.Contains(x.TurnoCajaId) ||
                 (x.MovimientoCajaId.HasValue && movementIds.Contains(x.MovimientoCajaId.Value))))
            .Select(x => new PaymentRow(x.TurnoCajaId, x.MovimientoCajaId,
                x.TipoMovimiento == "COBRO" ? "COBRO_CAMBIO_VENTA" : "DEVOLUCION_CAMBIO_VENTA",
                x.MetodoPago.Codigo, x.TipoMovimiento == "COBRO" ? x.Valor : -x.Valor, x.Estado,
                x.MovimientoCaja != null && x.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.ReembolsoApartado.AsNoTracking()
            .Where(x => x.TurnoCaja.Caja.InstalacionId == installationId &&
                (shiftIds.Contains(x.TurnoCajaId) ||
                 (x.MovimientoCajaId.HasValue && movementIds.Contains(x.MovimientoCajaId.Value))))
            .Select(x => new PaymentRow(x.TurnoCajaId, x.MovimientoCajaId, "REEMBOLSO_APARTADO",
                x.MetodoPago.Codigo, -x.Valor, "APLICADO",
                x.MovimientoCaja != null && x.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));

        var entries = new List<CashFlowEntry>();
        foreach (var payment in payments.Where(x => dates.ContainsKey(x.ShiftId) &&
                     (x.State == "APLICADO" || (x.State == "ANULADO" && x.HasReversal))))
            entries.Add(new CashFlowEntry(dates[payment.ShiftId], payment.Origin, payment.Method, payment.Amount));

        var linkedIds = payments.Where(x => x.MovementId.HasValue).Select(x => x.MovementId!.Value).ToHashSet();
        foreach (var movement in current.Where(x => !linkedIds.Contains(x.Id)))
        {
            if (movement.Type == "REVERSO" && movement.ReversedId is Guid reversedId &&
                allMovements.TryGetValue(reversedId, out var original))
            {
                var originalPayments = payments.Where(x => x.MovementId == reversedId).ToArray();
                if (originalPayments.Length > 0)
                {
                    if (movement.Amount != originalPayments.Sum(x => Math.Abs(x.Amount)))
                        throw new CashMoneyException();
                    foreach (var payment in originalPayments)
                        entries.Add(new CashFlowEntry(dates[movement.ShiftId], "REVERSO_" + payment.Origin,
                            payment.Method, -payment.Amount));
                    continue;
                }
                entries.Add(new CashFlowEntry(dates[movement.ShiftId], "REVERSO_" + ManualOrigin(original),
                    null, -SignedManualAmount(original)));
                continue;
            }
            entries.Add(new CashFlowEntry(dates[movement.ShiftId], ManualOrigin(movement), null,
                SignedManualAmount(movement)));
        }
        return entries;
    }

    private static string ManualOrigin(MovementRow x) => x.Type switch
    {
        "APERTURA" => "APERTURA",
        "INGRESO" => "INGRESO_MANUAL",
        "EGRESO" when x.Category == "SANGRIA" => "SANGRIA_MANUAL",
        "EGRESO" => "GASTO_MANUAL",
        _ => x.Category
    };

    private static decimal SignedManualAmount(MovementRow x) =>
        x.Type is "APERTURA" or "INGRESO" ? x.Amount : -x.Amount;

    private sealed record MovementRow(Guid Id, Guid ShiftId, string Type, string Category,
        decimal Amount, Guid? ReversedId);
    private sealed record PaymentRow(Guid ShiftId, Guid? MovementId, string Origin, string Method,
        decimal Amount, string State, bool HasReversal);
}
