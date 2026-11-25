using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Purchases.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfPurchaseRepository(OfficialControlPlusDbContext db) : IPurchaseRepository
{
    public async Task<IPurchaseTransaction> BeginAsync(CancellationToken ct = default) =>
        new Transaction(await db.Database.BeginTransactionAsync(ct));

    public async Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken ct = default) =>
        (await db.Instalacion.FromSqlRaw(
            "SELECT i.* FROM configuracion.instalacion AS i WHERE i.activo ORDER BY i.fecha_instalacion, i.id LIMIT 1 FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();

    public Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken ct = default) =>
        db.Terminal.AsNoTracking().Where(x => x.InstalacionId == installationId && x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);

    public async Task<Proveedor?> GetSupplierForUpdateAsync(Guid id, CancellationToken ct = default) =>
        (await db.Proveedor.FromSqlInterpolated(
            $"SELECT p.* FROM catalogo.proveedor AS p WHERE p.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();

    public async Task<IReadOnlyCollection<Producto>> GetProductsForUpdateAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        var ordered = ids.Distinct().Order().ToArray();
        return await db.Producto.FromSqlInterpolated(
            $"SELECT p.* FROM catalogo.producto AS p WHERE p.id = ANY ({ordered}) ORDER BY p.id FOR UPDATE")
            .AsTracking().ToArrayAsync(ct);
    }

    public async Task<Compra?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        var purchase = (await db.Compra.FromSqlInterpolated(
            $"SELECT c.* FROM compras.compra AS c WHERE c.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
        if (purchase is not null) await db.Entry(purchase).Collection(x => x.DetalleCompra).LoadAsync(ct);
        return purchase;
    }

    public async Task<PedidoCompra?> GetOrderForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        var order = (await db.PedidoCompra.FromSqlInterpolated(
            $"SELECT o.* FROM compras.pedido_compra AS o WHERE o.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
        if (order is not null) await db.Entry(order).Collection(x => x.DetallePedidoCompra).LoadAsync(ct);
        return order;
    }

    public Task<Compra?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Compra.AsNoTracking().Include(x => x.DetalleCompra).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PagedResult<Compra>> ListAsync(PurchaseListQuery query, int offset, CancellationToken ct = default)
    {
        var source = db.Compra.AsNoTracking().AsQueryable();
        if (query.SupplierId is Guid supplierId) source = source.Where(x => x.ProveedorId == supplierId);
        if (query.OrderId is Guid orderId) source = source.Where(x => x.PedidoCompraId == orderId);
        if (query.Status is not null) source = source.Where(x => x.Estado == query.Status);
        if (query.InvoiceDate is DateOnly date) source = source.Where(x => x.FechaDocumento == date);
        if (query.SupplierDocumentNumber is not null)
        {
            var documentPattern = PostgresLikePattern.Escape(query.SupplierDocumentNumber);
            source = source.Where(x => x.NumeroDocumentoProveedor != null &&
                EF.Functions.ILike(x.NumeroDocumentoProveedor, documentPattern, PostgresLikePattern.EscapeCharacter));
        }
        var count = await source.CountAsync(ct);
        var items = await source.OrderByDescending(x => x.FechaDocumento).ThenByDescending(x => x.Id)
            .Skip(offset).Take(query.PageSize).ToArrayAsync(ct);
        return new PagedResult<Compra>(items, query.Page, query.PageSize, count);
    }

    public async Task<ConsecutivoDocumento?> GetSequenceForUpdateAsync(Guid installationId, string series,
        CancellationToken ct = default)
    {
        // Confirming a purchase (or receiving an order) is the first use: the consecutive is created here
        // when it does not exist yet.
        await DocumentSequenceProvisioner.EnsureAsync(
            db, installationId, series, DocumentNumberingDefaults.Purchases, ct);
        return (await db.ConsecutivoDocumento.FromSqlInterpolated(
            $"SELECT c.* FROM configuracion.consecutivo_documento AS c WHERE c.instalacion_id = {installationId} AND c.tipo_documento = 'COMPRA' AND c.serie = {series} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
    }

    public Task<OperacionIdempotente?> GetIdempotencyAsync(Guid installationId, string key, CancellationToken ct = default) =>
        db.OperacionIdempotente.AsNoTracking().SingleOrDefaultAsync(
            x => x.InstalacionId == installationId && x.ClaveIdempotencia == key, ct);

    public Task<bool> DocumentExistsAsync(Guid supplierId, string document, Guid? excludingId,
        CancellationToken ct = default)
    {
        var documentPattern = PostgresLikePattern.Escape(document);
        return db.Compra.AsNoTracking().AnyAsync(x => x.ProveedorId == supplierId &&
            x.NumeroDocumentoProveedor != null &&
            EF.Functions.ILike(x.NumeroDocumentoProveedor, documentPattern, PostgresLikePattern.EscapeCharacter) &&
            (excludingId == null || x.Id != excludingId), ct);
    }

    public Task AddAsync(Compra purchase, CancellationToken ct = default) => db.Compra.AddAsync(purchase, ct).AsTask();

    public async Task ReplaceDetailsAsync(Compra purchase, IReadOnlyCollection<DetalleCompra> details,
        CancellationToken ct = default)
    {
        db.DetalleCompra.RemoveRange(purchase.DetalleCompra);
        purchase.DetalleCompra.Clear();
        await db.DetalleCompra.AddRangeAsync(details, ct);
        foreach (var detail in details) purchase.DetalleCompra.Add(detail);
    }

    public Task AddMovementsAsync(IReadOnlyCollection<MovimientoInventario> movements, CancellationToken ct = default) =>
        db.MovimientoInventario.AddRangeAsync(movements, ct);

    public Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken ct = default) =>
        db.OperacionIdempotente.AddAsync(operation, ct).AsTask();

    public async Task<IReadOnlyCollection<MotivoOperacion>> ListCancellationReasonsAsync(Guid establishmentId, CancellationToken ct = default) =>
        await db.MotivoOperacion.AsNoTracking().Where(x => x.EstablecimientoId == establishmentId &&
            x.TipoOperacion == "ANULACION_COMPRA").OrderBy(x => x.OrdenVisual).ThenBy(x => x.Id).ToArrayAsync(ct);

    public Task<MotivoOperacion?> GetCancellationReasonAsync(Guid establishmentId, Guid id, CancellationToken ct = default) =>
        db.MotivoOperacion.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            x.EstablecimientoId == establishmentId && x.TipoOperacion == "ANULACION_COMPRA" && x.Activo, ct);

    public Task AddCancellationReasonsAsync(IReadOnlyCollection<MotivoOperacion> reasons, CancellationToken ct = default) =>
        db.MotivoOperacion.AddRangeAsync(reasons, ct);

    public async Task<IReadOnlyCollection<MovimientoInventario>> GetPurchaseMovementsAsync(Guid purchaseId, CancellationToken ct = default) =>
        await db.MovimientoInventario.AsNoTracking().Where(x => x.CompraId == purchaseId &&
            x.TipoMovimiento == "COMPRA").ToArrayAsync(ct);

    public Task<bool> HasLaterMovementAsync(Guid productId, long originalOrder, CancellationToken ct = default) =>
        db.MovimientoInventario.AsNoTracking().AnyAsync(x => x.ProductoId == productId &&
            x.OrdenRegistro != null && x.OrdenRegistro > originalOrder, ct);

    public Task<bool> HasReversalAsync(Guid movementId, CancellationToken ct = default) =>
        db.MovimientoInventario.AsNoTracking().AnyAsync(x => x.MovimientoRevertidoId == movementId, ct);

    public async Task<Usuario?> GetUserForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        var user = (await db.Usuario.FromSqlInterpolated($"SELECT u.* FROM seguridad.usuario AS u WHERE u.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
        if (user is not null) await db.Entry(user).ReloadAsync(ct);
        return user;
    }

    public Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken ct = default) =>
        db.AutorizacionOperacion.AddAsync(authorization, ct).AsTask();

    public Task AddReceiptCorrectionAsync(CorreccionRecepcion correction, CancellationToken ct = default) =>
        db.Set<CorreccionRecepcion>().AddAsync(correction, ct).AsTask();

    private sealed class Transaction(IDbContextTransaction transaction) : IPurchaseTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
