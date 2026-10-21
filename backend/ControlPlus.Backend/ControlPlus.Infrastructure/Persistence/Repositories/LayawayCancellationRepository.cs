using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfLayawayCancellationRepository(OfficialControlPlusDbContext db)
    : ILayawayCancellationRepository
{
    public async Task<Apartado?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await db.Apartado.FromSqlInterpolated(
            $"SELECT a.* FROM ventas.apartado AS a WHERE a.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyCollection<DetalleApartado>> GetLinesAsync(Guid id,
        CancellationToken cancellationToken = default) =>
        await db.DetalleApartado.AsNoTracking().Where(x => x.ApartadoId == id)
            .OrderBy(x => x.ProductoId).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);

    public async Task<decimal> GetTotalPaidAsync(Guid layawayId, CancellationToken cancellationToken) =>
        await db.PagoApartado.AsNoTracking().Where(x => x.ApartadoId == layawayId && x.Estado == "APLICADO")
            .SumAsync(x => (decimal?)x.ValorAplicado, cancellationToken) ?? 0m;

    public Task<MotivoOperacion?> GetReasonAsync(Guid establishmentId, Guid reasonId,
        CancellationToken cancellationToken) => db.MotivoOperacion.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == reasonId && x.EstablecimientoId == establishmentId &&
                x.TipoOperacion == "CANCELACION_APARTADO" && x.Activo, cancellationToken);

    public Task AddRefundsAsync(IReadOnlyCollection<ReembolsoApartado> refunds,
        CancellationToken cancellationToken) => db.ReembolsoApartado.AddRangeAsync(refunds, cancellationToken);
}
