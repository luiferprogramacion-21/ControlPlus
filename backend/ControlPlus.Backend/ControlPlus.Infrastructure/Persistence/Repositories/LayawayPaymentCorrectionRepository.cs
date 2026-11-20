using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfLayawayPaymentCorrectionRepository(OfficialControlPlusDbContext db)
    : ILayawayPaymentCorrectionRepository
{
    public async Task<PagoApartado?> PaymentForUpdateAsync(Guid layawayId, Guid paymentId, CancellationToken ct) =>
        (await db.PagoApartado.FromSqlInterpolated($"SELECT p.* FROM ventas.pago_apartado p WHERE p.apartado_id={layawayId} AND p.id={paymentId} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
    public async Task<IReadOnlyCollection<PagoApartado>> MovementPaymentsForUpdateAsync(Guid movementId, CancellationToken ct) =>
        await db.PagoApartado.FromSqlInterpolated($"SELECT p.* FROM ventas.pago_apartado p WHERE p.movimiento_caja_id={movementId} ORDER BY p.id FOR UPDATE")
            .AsTracking().ToArrayAsync(ct);
    public async Task<MovimientoCaja?> MovementForUpdateAsync(Guid movementId, CancellationToken ct) =>
        (await db.MovimientoCaja.FromSqlInterpolated($"SELECT m.* FROM caja.movimiento_caja m WHERE m.id={movementId} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
    public Task<bool> HasReversalAsync(Guid movementId, CancellationToken ct) =>
        db.MovimientoCaja.AnyAsync(m => m.MovimientoRevertidoId == movementId, ct);
    public async Task<IReadOnlyCollection<MetodoPago>> MethodsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.MetodoPago.AsNoTracking().Where(m => ids.Contains(m.Id)).ToArrayAsync(ct);
    public async Task<IReadOnlyCollection<DetalleApartado>> LinesForUpdateAsync(Guid layawayId, CancellationToken ct) =>
        await db.DetalleApartado.FromSqlInterpolated($"SELECT d.* FROM ventas.detalle_apartado d WHERE d.apartado_id={layawayId} AND d.vigente ORDER BY d.producto_id FOR UPDATE")
            .AsTracking().ToArrayAsync(ct);
    public Task AppendLinesAsync(IReadOnlyCollection<DetalleApartado> newLines, CancellationToken ct) => db.DetalleApartado.AddRangeAsync(newLines, ct);
    public async Task AddCorrectionAsync(CorreccionApartado correction, IReadOnlyCollection<ReembolsoCorreccionApartado> refunds, CancellationToken ct)
    { await db.CorreccionApartado.AddAsync(correction, ct); await db.ReembolsoCorreccionApartado.AddRangeAsync(refunds, ct); }
    public async Task<IReadOnlyCollection<CorreccionApartado>> HistoryAsync(Guid layawayId, CancellationToken ct) =>
        await db.CorreccionApartado.AsNoTracking().Where(c => c.ApartadoId == layawayId).OrderBy(c => c.VersionPosterior).ToArrayAsync(ct);
}
