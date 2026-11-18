using ControlPlus.Application.Sales.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>Borradores de venta (<c>ventas.borrador_venta</c> y su detalle), tablas oficiales de Fase 4.</summary>
public sealed class EfSaleDraftRepository(OfficialControlPlusDbContext dbContext) : ISaleDraftRepository
{
    private static readonly string[] ActiveStates = ["ACTIVO", "RECUPERADO"];

    public async Task<SaleDraftScope?> GetScopeAsync(CancellationToken cancellationToken = default)
    {
        var installationId = await dbContext.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (installationId is null) return null;
        var shift = await dbContext.TurnoCaja.AsNoTracking().Where(x => x.Estado == "ABIERTA")
            .OrderBy(x => x.FechaHoraApertura).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.TerminalId }).FirstOrDefaultAsync(cancellationToken);
        return shift is null ? null : new SaleDraftScope(installationId.Value, shift.TerminalId, shift.Id);
    }

    public Task<BorradorVenta?> GetActiveAsync(Guid userId, Guid shiftId, bool track, CancellationToken cancellationToken = default)
    {
        var query = dbContext.BorradorVenta.Include(x => x.DetalleBorradorVenta)
            .Where(x => x.UsuarioId == userId && x.TurnoCajaId == shiftId && ActiveStates.Contains(x.Estado))
            .OrderByDescending(x => x.FechaModificacion ?? x.FechaCreacion).ThenByDescending(x => x.Id);
        return (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Producto>> GetProductsAsync(IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToArray();
        return await dbContext.Producto.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(cancellationToken);
    }

    public Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        dbContext.Cliente.AsNoTracking().AnyAsync(x => x.Id == customerId, cancellationToken);

    public async Task SaveAsync(BorradorVenta draft, bool isNew, IReadOnlyCollection<DetalleBorradorVenta> lines,
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Otros turnos ya no se pueden recuperar: se descartan para no acumular borradores activos.
        await dbContext.BorradorVenta
            .Where(x => x.UsuarioId == draft.UsuarioId && x.TurnoCajaId != draft.TurnoCajaId && ActiveStates.Contains(x.Estado))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Estado, "DESCARTADO").SetProperty(x => x.FechaCierre, nowUtc),
                cancellationToken);
        if (isNew)
        {
            await dbContext.BorradorVenta.AddAsync(draft, cancellationToken);
        }
        else
        {
            // Las líneas se reemplazan completas; se borran antes para no chocar con los únicos de producto y orden.
            await dbContext.DetalleBorradorVenta.Where(x => x.BorradorVentaId == draft.Id).ExecuteDeleteAsync(cancellationToken);
            foreach (var old in draft.DetalleBorradorVenta.ToArray()) dbContext.Entry(old).State = EntityState.Detached;
            draft.DetalleBorradorVenta.Clear();
        }

        await dbContext.DetalleBorradorVenta.AddRangeAsync(lines, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task DiscardActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        dbContext.BorradorVenta.Where(x => x.UsuarioId == userId && ActiveStates.Contains(x.Estado))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Estado, "DESCARTADO").SetProperty(x => x.FechaCierre, nowUtc),
                cancellationToken);

    public async Task ConvertActiveAsync(Guid userId, Guid shiftId, Guid saleId, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var active = await dbContext.BorradorVenta
            .Where(x => x.UsuarioId == userId && x.TurnoCajaId == shiftId && ActiveStates.Contains(x.Estado))
            .OrderByDescending(x => x.FechaModificacion ?? x.FechaCreacion).ThenByDescending(x => x.Id)
            .ToArrayAsync(cancellationToken);
        for (var index = 0; index < active.Length; index++)
        {
            var draft = active[index];
            draft.FechaCierre = nowUtc;
            draft.FechaModificacion = nowUtc;
            if (index == 0) { draft.Estado = "CONVERTIDO"; draft.VentaConfirmadaId = saleId; }
            else draft.Estado = "DESCARTADO";
        }
    }
}
