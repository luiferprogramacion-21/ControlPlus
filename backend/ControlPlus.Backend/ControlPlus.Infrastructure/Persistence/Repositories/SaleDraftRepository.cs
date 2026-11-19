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

    public async Task<IReadOnlyCollection<BorradorVenta>> GetOpenAsync(Guid userId, Guid shiftId,
        CancellationToken cancellationToken = default) =>
        await dbContext.BorradorVenta.AsNoTracking().Include(x => x.DetalleBorradorVenta).Include(x => x.Cliente)
            .Where(x => x.UsuarioId == userId && x.TurnoCajaId == shiftId && ActiveStates.Contains(x.Estado))
            .OrderByDescending(x => x.FechaModificacion ?? x.FechaCreacion).ThenByDescending(x => x.Id)
            .ToArrayAsync(cancellationToken);

    public Task<BorradorVenta?> GetOpenByIdAsync(Guid draftId, Guid userId, Guid shiftId, bool track,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.BorradorVenta.Include(x => x.DetalleBorradorVenta).Where(x =>
            x.Id == draftId && x.UsuarioId == userId && x.TurnoCajaId == shiftId && ActiveStates.Contains(x.Estado));
        return (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int> CountOpenAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken = default) =>
        dbContext.BorradorVenta.CountAsync(x =>
            x.UsuarioId == userId && x.TurnoCajaId == shiftId && ActiveStates.Contains(x.Estado), cancellationToken);

    public async Task<bool> DiscardAsync(Guid draftId, Guid userId, DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        await dbContext.BorradorVenta
            .Where(x => x.Id == draftId && x.UsuarioId == userId && ActiveStates.Contains(x.Estado))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Estado, "DESCARTADO").SetProperty(x => x.FechaCierre, nowUtc),
                cancellationToken) > 0;

    public async Task ConvertAsync(Guid userId, Guid shiftId, Guid saleId, Guid? draftId,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> saleLines, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var open = dbContext.BorradorVenta.Include(x => x.DetalleBorradorVenta)
            .Where(x => x.UsuarioId == userId && x.TurnoCajaId == shiftId && ActiveStates.Contains(x.Estado));
        var draft = draftId is Guid id
            ? await open.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            : await open.OrderByDescending(x => x.FechaModificacion ?? x.FechaCreacion).ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        // Sin el borrador indicado, solo se cierra el más reciente si es esta misma venta: una venta en espera con otros
        // productos sigue abierta.
        if (draft is null || draftId is null && !SameLines(draft, saleLines)) return;
        draft.Estado = "CONVERTIDO";
        draft.VentaConfirmadaId = saleId;
        draft.FechaCierre = nowUtc;
        draft.FechaModificacion = nowUtc;
    }

    private static bool SameLines(BorradorVenta draft, IReadOnlyCollection<(Guid ProductId, int Quantity)> saleLines) =>
        draft.DetalleBorradorVenta.GroupBy(x => x.ProductoId).Select(x => (ProductId: x.Key, Quantity: x.Sum(y => y.Cantidad)))
            .OrderBy(x => x.ProductId)
            .SequenceEqual(saleLines.GroupBy(x => x.ProductId).Select(x => (ProductId: x.Key, Quantity: x.Sum(y => y.Quantity)))
                .OrderBy(x => x.ProductId));
}
