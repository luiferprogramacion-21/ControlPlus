using ControlPlus.Application.Common;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Application.Inventory.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfInventoryRepository(OfficialControlPlusDbContext dbContext) : IInventoryRepository
{
    public async Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Instalacion.FromSqlRaw(
            """
            SELECT i.*
            FROM configuracion.instalacion AS i
            WHERE i.activo
            ORDER BY i.fecha_instalacion, i.id
            LIMIT 1
            FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<Terminal?> GetActiveTerminalAsync(
        Guid installationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Terminal.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<InventoryStockRecord>> ListStockAsync(
        InventoryStockQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.Producto.AsNoTracking().AsQueryable();
        if (!query.IncludeInactive) source = source.Where(x => x.Activo);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search}%";
            source = source.Where(x =>
                EF.Functions.ILike(x.CodigoInterno, pattern) ||
                EF.Functions.ILike(x.Nombre, pattern) ||
                (x.CodigoBarras != null && EF.Functions.ILike(x.CodigoBarras, pattern)));
        }

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderBy(x => x.Nombre).ThenBy(x => x.CodigoInterno)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new InventoryStockRecord(
                x,
                x.UnidadMedida.Nombre,
                x.MovimientoInventario.OrderByDescending(m => m.FechaHora).ThenByDescending(m => m.Id)
                    .Select(m => m.TipoMovimiento).FirstOrDefault(),
                x.MovimientoInventario.OrderByDescending(m => m.FechaHora).ThenByDescending(m => m.Id)
                    .Select(m => (DateTime?)m.FechaHora).FirstOrDefault()))
            .ToArrayAsync(cancellationToken);
        return new PagedResult<InventoryStockRecord>(items, query.Page, query.PageSize, count);
    }

    public async Task<PagedResult<MovimientoInventario>> ListMovementsAsync(
        InventoryMovementQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.MovimientoInventario.AsNoTracking()
            .Include(x => x.Producto)
            .Include(x => x.MotivoOperacion)
            .AsQueryable();
        if (query.ProductId is Guid productId) source = source.Where(x => x.ProductoId == productId);
        if (query.Type is { } type) source = source.Where(x => x.TipoMovimiento == type);
        if (query.FromUtc is { } from) source = source.Where(x => x.FechaHora >= from.UtcDateTime);
        if (query.ToUtc is { } to) source = source.Where(x => x.FechaHora <= to.UtcDateTime);

        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<MovimientoInventario>(items, query.Page, query.PageSize, count);
    }

    public async Task<IReadOnlyCollection<MotivoOperacion>> ListActiveAdjustmentReasonsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.MotivoOperacion.AsNoTracking()
            .Where(x => x.Activo && x.Establecimiento.Activo && x.TipoOperacion == "AJUSTE_INVENTARIO")
            .OrderBy(x => x.OrdenVisual).ThenBy(x => x.Codigo)
            .ToArrayAsync(cancellationToken);

    public Task<MotivoOperacion?> GetActiveAdjustmentReasonAsync(
        Guid reasonId,
        CancellationToken cancellationToken = default) =>
        dbContext.MotivoOperacion.SingleOrDefaultAsync(
            x => x.Id == reasonId && x.Activo && x.Establecimiento.Activo &&
                 x.TipoOperacion == "AJUSTE_INVENTARIO",
            cancellationToken);

    public async Task<Producto?> GetProductForUpdateAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Producto.FromSqlInterpolated(
            $"""
             SELECT p.*
             FROM catalogo.producto AS p
             WHERE p.id = {productId}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<Usuario?> GetUserForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Usuario.FromSqlInterpolated(
            $"""
             SELECT u.*
             FROM seguridad.usuario AS u
             WHERE u.id = {userId}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<OperacionIdempotente?> GetIdempotencyAsync(
        Guid installationId,
        string key,
        CancellationToken cancellationToken = default) =>
        dbContext.OperacionIdempotente.AsNoTracking().SingleOrDefaultAsync(
            x => x.InstalacionId == installationId && x.ClaveIdempotencia == key,
            cancellationToken);

    public Task<MovimientoInventario?> GetMovementAsync(
        Guid movementId,
        CancellationToken cancellationToken = default) =>
        dbContext.MovimientoInventario.AsNoTracking()
            .Include(x => x.Producto)
            .Include(x => x.MotivoOperacion)
            .SingleOrDefaultAsync(x => x.Id == movementId, cancellationToken);

    public Task AddAuthorizationAsync(
        AutorizacionOperacion authorization,
        CancellationToken cancellationToken = default) =>
        dbContext.AutorizacionOperacion.AddAsync(authorization, cancellationToken).AsTask();

    public Task AddMovementAsync(
        MovimientoInventario movement,
        CancellationToken cancellationToken = default) =>
        dbContext.MovimientoInventario.AddAsync(movement, cancellationToken).AsTask();

    public Task AddIdempotencyAsync(
        OperacionIdempotente operation,
        CancellationToken cancellationToken = default) =>
        dbContext.OperacionIdempotente.AddAsync(operation, cancellationToken).AsTask();
}
