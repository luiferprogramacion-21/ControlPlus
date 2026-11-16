using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.PurchaseOrders.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfPurchaseOrderRepository(OfficialControlPlusDbContext db) : IPurchaseOrderRepository
{
    public async Task<IPurchaseOrderTransaction> BeginAsync(CancellationToken ct = default) =>
        new Transaction(await db.Database.BeginTransactionAsync(ct));

    public async Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken ct = default) =>
        (await db.Instalacion.FromSqlRaw(
            "SELECT i.* FROM configuracion.instalacion AS i WHERE i.activo ORDER BY i.fecha_instalacion, i.id LIMIT 1 FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();

    public Task<Instalacion?> GetActiveInstallationAsync(CancellationToken ct = default) =>
        db.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);

    public async Task<Proveedor?> GetSupplierForUpdateAsync(Guid id, CancellationToken ct = default) =>
        (await db.Proveedor.FromSqlInterpolated(
            $"SELECT p.* FROM catalogo.proveedor AS p WHERE p.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();

    public async Task<IReadOnlyCollection<Producto>> GetProductsForUpdateAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var ordered = ids.Distinct().Order().ToArray();
        return await db.Producto.FromSqlInterpolated(
            $"SELECT p.* FROM catalogo.producto AS p WHERE p.id = ANY ({ordered}) ORDER BY p.id FOR UPDATE")
            .AsTracking().ToArrayAsync(ct);
    }

    public async Task<ConsecutivoDocumento?> GetNumberingForUpdateAsync(Guid installationId,
        string series, CancellationToken ct = default) =>
        (await db.ConsecutivoDocumento.FromSqlInterpolated(
            $"SELECT c.* FROM configuracion.consecutivo_documento AS c WHERE c.instalacion_id = {installationId} AND c.tipo_documento = 'PEDIDO_COMPRA' AND c.serie = {series} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();

    public Task<ConsecutivoDocumento?> GetNumberingAsync(Guid installationId,
        string series, CancellationToken ct = default) =>
        db.ConsecutivoDocumento.AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstalacionId == installationId && x.TipoDocumento == "PEDIDO_COMPRA" && x.Serie == series, ct);

    public Task AddNumberingAsync(ConsecutivoDocumento numbering, CancellationToken ct = default) =>
        db.ConsecutivoDocumento.AddAsync(numbering, ct).AsTask();

    public Task EnsureNumberingAsync(Guid installationId, string series, CancellationToken ct = default) =>
        DocumentSequenceProvisioner.EnsureAsync(
            db, installationId, series, DocumentNumberingDefaults.PurchaseOrders, ct);

    public async Task<PedidoCompra?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        var order = (await db.PedidoCompra.FromSqlInterpolated(
            $"SELECT o.* FROM compras.pedido_compra AS o WHERE o.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
        if (order is not null) await db.Entry(order).Collection(x => x.DetallePedidoCompra).LoadAsync(ct);
        return order;
    }

    public Task<PedidoCompra?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.PedidoCompra.AsNoTracking().Include(x => x.DetallePedidoCompra)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PagedResult<PedidoCompra>> ListAsync(Guid installationId,
        PurchaseOrderListQuery query, int offset, CancellationToken ct = default)
    {
        var source = db.PedidoCompra.AsNoTracking().Include(x => x.DetallePedidoCompra)
            .Where(x => x.InstalacionId == installationId);
        if (query.SupplierId is Guid supplierId) source = source.Where(x => x.ProveedorId == supplierId);
        if (query.Status is not null) source = source.Where(x => x.Estado == query.Status);
        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.FechaCreacion).ThenByDescending(x => x.Id)
            .Skip(offset).Take(query.PageSize).ToArrayAsync(ct);
        return new PagedResult<PedidoCompra>(items, query.Page, query.PageSize, count);
    }

    public Task<bool> HasDraftForSupplierAsync(Guid installationId, Guid supplierId,
        Guid? excludingOrderId, CancellationToken ct = default) =>
        db.PedidoCompra.AsNoTracking().AnyAsync(x => x.InstalacionId == installationId &&
            x.ProveedorId == supplierId && x.Estado == "BORRADOR" &&
            (excludingOrderId == null || x.Id != excludingOrderId), ct);

    public async Task<IReadOnlyDictionary<Guid, int>> PendingOtherOrdersAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToArray();
        var pending = await db.DetallePedidoCompra.AsNoTracking().Where(x => ids.Contains(x.ProductoId) &&
                (x.PedidoCompra.Estado == "ENVIADO" || x.PedidoCompra.Estado == "RECIBIDO_PARCIAL"))
            .Select(x => new { x.ProductoId, x.CantidadSolicitada, x.CantidadRecibida })
            .ToArrayAsync(ct);
        return pending.GroupBy(x => x.ProductoId).ToDictionary(x => x.Key,
            x => checked(x.Sum(line => Math.Max(0, line.CantidadSolicitada - line.CantidadRecibida))));
    }

    public Task AddAsync(PedidoCompra order, CancellationToken ct = default) =>
        db.PedidoCompra.AddAsync(order, ct).AsTask();

    public async Task ReplaceLinesAsync(PedidoCompra order,
        IReadOnlyCollection<DetallePedidoCompra> lines, CancellationToken ct = default)
    {
        var retained = lines.ToDictionary(x => x.ProductoId);
        foreach (var old in order.DetallePedidoCompra.ToArray())
        {
            if (!retained.Remove(old.ProductoId, out var replacement))
            {
                db.DetallePedidoCompra.Remove(old);
                order.DetallePedidoCompra.Remove(old);
                continue;
            }
            old.CodigoProducto = replacement.CodigoProducto;
            old.NombreProducto = replacement.NombreProducto;
            old.StockDisponibleOrigen = replacement.StockDisponibleOrigen;
            old.StockMinimoOrigen = replacement.StockMinimoOrigen;
            old.PendienteOtrosPedidos = replacement.PendienteOtrosPedidos;
            old.CantidadSugerida = replacement.CantidadSugerida;
            old.CantidadSolicitada = replacement.CantidadSolicitada;
            old.OrigenNecesidad = replacement.OrigenNecesidad;
            old.CostoEstimado = replacement.CostoEstimado;
            old.Observacion = replacement.Observacion;
        }
        foreach (var added in retained.Values)
        {
            await db.DetallePedidoCompra.AddAsync(added, ct);
            order.DetallePedidoCompra.Add(added);
        }
    }

    public async Task<IReadOnlyCollection<MotivoOperacion>> ListReasonsAsync(Guid establishmentId,
        string type, CancellationToken ct = default) => await db.MotivoOperacion.AsNoTracking()
        .Where(x => x.EstablecimientoId == establishmentId && x.TipoOperacion == type)
        .OrderBy(x => x.OrdenVisual).ThenBy(x => x.Id).ToArrayAsync(ct);

    public Task<MotivoOperacion?> GetReasonAsync(Guid establishmentId, string type,
        Guid id, CancellationToken ct = default) => db.MotivoOperacion.AsNoTracking()
        .SingleOrDefaultAsync(x => x.EstablecimientoId == establishmentId && x.TipoOperacion == type &&
            x.Id == id && x.Activo, ct);

    public Task AddReasonsAsync(IReadOnlyCollection<MotivoOperacion> reasons, CancellationToken ct = default) =>
        db.MotivoOperacion.AddRangeAsync(reasons, ct);

    public async Task<Usuario?> GetUserForUpdateAsync(Guid id, CancellationToken ct = default) =>
        (await db.Usuario.FromSqlInterpolated($"SELECT u.* FROM seguridad.usuario AS u WHERE u.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();

    public Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken ct = default) =>
        db.AutorizacionOperacion.AddAsync(authorization, ct).AsTask();

    private sealed class Transaction(IDbContextTransaction transaction) : IPurchaseOrderTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
