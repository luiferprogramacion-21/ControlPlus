using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Catalog.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfCatalogRepository(OfficialControlPlusDbContext dbContext) : ICatalogRepository
{
    public Task<string> AllocateProductReferenceAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        dbContext.Database.SqlQuery<string>($"SELECT catalogo.siguiente_referencia_producto({categoryId}) AS \"Value\"").SingleAsync(cancellationToken);

    public Task<bool> CategoryAbbreviationExistsAsync(string abbreviation, Guid? excludedId = null, CancellationToken cancellationToken = default) =>
        dbContext.Categoria.AnyAsync(x => x.Abreviatura == abbreviation && (excludedId == null || x.Id != excludedId), cancellationToken);

    public async Task AddProductCodeAliasAsync(Guid productId, string code, CancellationToken cancellationToken = default) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT catalogo.reservar_codigo_producto({code}, {productId})", cancellationToken);
    public async Task<ICatalogTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new CatalogTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public async Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Instalacion.FromSqlRaw(
            "SELECT i.* FROM configuracion.instalacion AS i WHERE i.activo ORDER BY i.fecha_instalacion, i.id LIMIT 1 FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken cancellationToken = default) =>
        dbContext.Terminal.AsNoTracking().Where(x => x.InstalacionId == installationId && x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<OperacionIdempotente?> GetIdempotencyAsync(Guid installationId, string key, CancellationToken cancellationToken = default) =>
        dbContext.OperacionIdempotente.AsNoTracking().SingleOrDefaultAsync(
            x => x.InstalacionId == installationId && x.ClaveIdempotencia == key, cancellationToken);

    public Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken cancellationToken = default) =>
        dbContext.OperacionIdempotente.AddAsync(operation, cancellationToken).AsTask();

    public Task AddMovementAsync(MovimientoInventario movement, CancellationToken cancellationToken = default) =>
        dbContext.MovimientoInventario.AddAsync(movement, cancellationToken).AsTask();

    private sealed class CatalogTransaction(IDbContextTransaction transaction) : ICatalogTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    public async Task<PagedResult<Categoria>> ListCategoriesAsync(
        CategoryListQuery query, CancellationToken cancellationToken = default)
    {
        var categories = dbContext.Categoria.AsNoTracking().AsQueryable();
        if (!query.IncludeInactive) categories = categories.Where(x => x.Activo);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            categories = categories.Where(x => EF.Functions.ILike(x.Nombre, pattern));
        }
        var count = await categories.CountAsync(cancellationToken);
        var items = await categories.OrderBy(x => x.Nombre)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Categoria>(items, query.Page, query.PageSize, count);
    }

    public Task<Categoria?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        dbContext.Categoria.SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

    public Task<bool> CategoryNameExistsAsync(
        string name, Guid? excludedId = null, CancellationToken cancellationToken = default) =>
        dbContext.Categoria.AnyAsync(x => EF.Functions.ILike(x.Nombre, name) &&
            (excludedId == null || x.Id != excludedId), cancellationToken);

    public Task<bool> CategoryHasActiveProductsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        dbContext.Producto.AnyAsync(x => x.CategoriaId == categoryId && x.Activo, cancellationToken);

    public Task AddCategoryAsync(Categoria category, CancellationToken cancellationToken = default) =>
        dbContext.Categoria.AddAsync(category, cancellationToken).AsTask();

    public async Task<IReadOnlyCollection<UnidadMedida>> ListMeasurementUnitsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.UnidadMedida.AsNoTracking().Where(x => x.Activo).OrderBy(x => x.Nombre).ToArrayAsync(cancellationToken);

    public Task<UnidadMedida?> GetMeasurementUnitAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.UnidadMedida.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ActiveSupplierExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Proveedor.AnyAsync(x => x.Id == id && x.Activo, cancellationToken);

    public async Task<PagedResult<Producto>> ListProductsAsync(
        ProductListQuery query, CancellationToken cancellationToken = default)
    {
        var products = dbContext.Producto.AsNoTracking()
            .Include(x => x.Categoria).Include(x => x.UnidadMedida).AsQueryable();
        if (!query.IncludeInactive) products = products.Where(x => x.Activo);
        if (query.CategoryId is Guid categoryId) products = products.Where(x => x.CategoriaId == categoryId);
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);
        products = query.StockStatus switch
        {
            // Mismo criterio que Inventario y el Dashboard: «stock bajo» es disponible entre 1 y el mínimo; los agotados van aparte.
            InventoryStockStatus.LowStock => products.Where(LowStockSelection.Predicate),
            InventoryStockStatus.OutOfStock => products.Where(x => x.StockActual - x.StockReservado <= 0),
            InventoryStockStatus.Available => products.Where(x => x.StockActual - x.StockReservado > x.StockMinimo),
            _ when !query.IncludeOutOfStock && !hasSearch => products.Where(x => x.StockActual - x.StockReservado > 0),
            _ => products
        };
        if (hasSearch)
        {
            var term = query.Search!.Trim();
            var pattern = $"%{term}%";
            var exactPattern = PostgresLikePattern.Escape(term);
            var exactId = await dbContext.ProductoCodigo
                .Where(c => EF.Functions.ILike(c.Codigo, exactPattern, PostgresLikePattern.EscapeCharacter))
                .Select(c => (Guid?)c.ProductoId).SingleOrDefaultAsync(cancellationToken);
            products = exactId is not null ? products.Where(x => x.Id == exactId) :
                products.Where(x => EF.Functions.ILike(x.Nombre, pattern) ||
                    dbContext.ProductoCodigo.Any(c => c.ProductoId == x.Id && EF.Functions.ILike(c.Codigo, pattern)));
        }
        var count = await products.CountAsync(cancellationToken);
        var items = await products.OrderBy(x => x.Nombre).ThenBy(x => x.CodigoInterno)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Producto>(items, query.Page, query.PageSize, count);
    }

    public Task<Producto?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default) =>
        dbContext.Producto.Include(x => x.Categoria).Include(x => x.UnidadMedida)
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);

    public async Task<Producto?> GetProductForUpdateAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Producto.FromSqlInterpolated(
            $"SELECT * FROM catalogo.producto WHERE id = {productId} FOR UPDATE")
            .AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<bool> HasProductHistoryAsync(Guid productId, CancellationToken cancellationToken = default) =>
        await dbContext.DetalleVenta.AnyAsync(x => x.ProductoId == productId, cancellationToken) ||
        await dbContext.DetalleCambioVenta.AnyAsync(x => x.ProductoId == productId, cancellationToken) ||
        await dbContext.DetalleCompra.AnyAsync(x => x.ProductoId == productId, cancellationToken) ||
        await dbContext.DetallePedidoCompra.AnyAsync(x => x.ProductoId == productId, cancellationToken) ||
        await dbContext.DetalleApartado.AnyAsync(x => x.ProductoId == productId, cancellationToken) ||
        await dbContext.DetalleBorradorVenta.AnyAsync(x => x.ProductoId == productId, cancellationToken) ||
        await dbContext.MovimientoInventario.AnyAsync(x => x.ProductoId == productId && x.TipoMovimiento != "INVENTARIO_INICIAL", cancellationToken);

    public async Task DeleteProductOwnedDataAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        await dbContext.MovimientoInventario.Where(x => x.ProductoId == productId && x.TipoMovimiento == "INVENTARIO_INICIAL")
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ProductoCodigo.Where(x => x.ProductoId == productId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Producto.Where(x => x.Id == productId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<PagedResult<DetalleCompra>> ListProductPurchaseCostsAsync(
        Guid productId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var details = dbContext.DetalleCompra.AsNoTracking()
            .Include(x => x.Compra)
            .Where(x => x.ProductoId == productId && x.Compra.Estado != "BORRADOR");
        var count = await details.CountAsync(cancellationToken);
        var items = await details
            .OrderByDescending(x => x.Compra.FechaHoraConfirmacion)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<DetalleCompra>(items, page, pageSize, count);
    }

    public Task<decimal?> GetLastConfirmedPurchaseCostAsync(
        Guid productId, CancellationToken cancellationToken = default) =>
        dbContext.DetalleCompra.AsNoTracking()
            .Where(x => x.ProductoId == productId && x.Compra.Estado == "CONFIRMADA")
            .OrderByDescending(x => x.Compra.FechaHoraConfirmacion)
            .Select(x => (decimal?)x.CostoUnitarioDocumento)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ProductInternalCodeExistsAsync(
        string code, Guid? excludedId = null, CancellationToken cancellationToken = default)
    {
        var pattern = PostgresLikePattern.Escape(code.Trim());
        return dbContext.ProductoCodigo.AnyAsync(x =>
            EF.Functions.ILike(x.Codigo, pattern, PostgresLikePattern.EscapeCharacter) &&
            (excludedId == null || x.ProductoId != excludedId), cancellationToken);
    }

    public Task<bool> ProductBarcodeExistsAsync(
        string barcode, Guid? excludedId = null, CancellationToken cancellationToken = default)
    {
        var pattern = PostgresLikePattern.Escape(barcode.Trim());
        return dbContext.ProductoCodigo.AnyAsync(x =>
            EF.Functions.ILike(x.Codigo, pattern, PostgresLikePattern.EscapeCharacter) &&
            (excludedId == null || x.ProductoId != excludedId), cancellationToken);
    }

    public Task AddProductAsync(Producto product, CancellationToken cancellationToken = default) =>
        dbContext.Producto.AddAsync(product, cancellationToken).AsTask();
}
