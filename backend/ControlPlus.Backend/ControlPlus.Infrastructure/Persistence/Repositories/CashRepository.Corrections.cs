using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed partial class EfCashRepository
{
    public async Task<MovimientoCaja?> GetMovementForCorrectionAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await dbContext.MovimientoCaja.FromSqlInterpolated($"SELECT * FROM caja.movimiento_caja WHERE id={id} FOR UPDATE")
            .Include(x => x.TurnoCaja).ThenInclude(x => x.Caja).AsTracking().ToArrayAsync(ct);
        return rows.SingleOrDefault();
    }
    public Task<bool> HasMovementReversalAsync(Guid id, CancellationToken ct = default) =>
        dbContext.MovimientoCaja.AnyAsync(x => x.MovimientoRevertidoId == id, ct);
    public Task<bool> HasMovementPaymentsAsync(Guid id, CancellationToken ct = default) =>
        dbContext.MovimientoCaja.Where(x => x.Id == id).AnyAsync(x => x.PagoVenta.Any() || x.PagoCredito.Any() ||
            x.PagoApartado.Any() || x.PagoCambioVenta.Any() || x.ReembolsoApartado.Any(), ct);
    public Task<OperacionIdempotente?> GetCorrectionIdempotencyAsync(Guid installationId, string key, CancellationToken ct = default) =>
        dbContext.OperacionIdempotente.SingleOrDefaultAsync(x => x.InstalacionId == installationId && x.ClaveIdempotencia == key, ct);
    public Task AddCorrectionIdempotencyAsync(OperacionIdempotente operation, CancellationToken ct = default) =>
        dbContext.OperacionIdempotente.AddAsync(operation, ct).AsTask();
    public async Task<IReadOnlyCollection<TurnoCaja>> ListClosedShiftsAsync(Guid installationId, CancellationToken ct = default) =>
        await dbContext.TurnoCaja.AsNoTracking().Where(x => x.Caja.InstalacionId == installationId && x.Estado == "CERRADA")
            .OrderByDescending(x => x.FechaHoraCierre).ThenByDescending(x => x.Id).Take(30).ToArrayAsync(ct);
    public async Task<IReadOnlyCollection<RevisionCierreCaja>> ListClosureReviewsAsync(Guid shiftId, CancellationToken ct = default) =>
        await dbContext.Set<RevisionCierreCaja>().AsNoTracking().Where(x => x.TurnoCajaId == shiftId)
            .OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id).ToArrayAsync(ct);
    public Task AddClosureReviewAsync(RevisionCierreCaja review, CancellationToken ct = default) =>
        dbContext.Set<RevisionCierreCaja>().AddAsync(review, ct).AsTask();
}
