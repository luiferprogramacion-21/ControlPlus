using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Catalog.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfCatalogRepository(OfficialControlPlusDbContext dbContext) : ICatalogRepository
{
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
        if (!query.IncludeOutOfStock && !hasSearch) products = products.Where(x => x.StockActual - x.StockReservado > 0);
        if (hasSearch)
        {
            var term = query.Search!.Trim();
            var pattern = $"%{term}%";
            products = products.Where(x => x.CodigoInterno == term.ToUpper() || x.CodigoBarras == term ||
                EF.Functions.ILike(x.Nombre, pattern));
        }
        var count = await products.CountAsync(cancellationToken);
        var items = await products.OrderBy(x => x.Nombre).ThenBy(x => x.CodigoInterno)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Producto>(items, query.Page, query.PageSize, count);
    }

    public Task<Producto?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default) =>
        dbContext.Producto.Include(x => x.Categoria).Include(x => x.UnidadMedida)
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);

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
        string code, Guid? excludedId = null, CancellationToken cancellationToken = default) =>
        dbContext.Producto.AnyAsync(x => x.CodigoInterno == code && (excludedId == null || x.Id != excludedId), cancellationToken);

    public Task<bool> ProductBarcodeExistsAsync(
        string barcode, Guid? excludedId = null, CancellationToken cancellationToken = default) =>
        dbContext.Producto.AnyAsync(x => x.CodigoBarras == barcode && (excludedId == null || x.Id != excludedId), cancellationToken);

    public Task AddProductAsync(Producto product, CancellationToken cancellationToken = default) =>
        dbContext.Producto.AddAsync(product, cancellationToken).AsTask();
}
