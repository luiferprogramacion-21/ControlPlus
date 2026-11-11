using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Catalog.Ports;

public interface ICatalogRepository
{
    Task<string> AllocateProductReferenceAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<bool> CategoryAbbreviationExistsAsync(string abbreviation, Guid? excludedId = null, CancellationToken cancellationToken = default);
    Task AddProductCodeAliasAsync(Guid productId, string code, CancellationToken cancellationToken = default);
    Task<ICatalogTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default);
    Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken cancellationToken = default);
    Task<OperacionIdempotente?> GetIdempotencyAsync(Guid installationId, string key, CancellationToken cancellationToken = default);
    Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken cancellationToken = default);
    Task AddMovementAsync(MovimientoInventario movement, CancellationToken cancellationToken = default);
    Task<PagedResult<Categoria>> ListCategoriesAsync(CategoryListQuery query, CancellationToken cancellationToken = default);
    Task<Categoria?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<bool> CategoryNameExistsAsync(string name, Guid? excludedId = null, CancellationToken cancellationToken = default);
    Task<bool> CategoryHasActiveProductsAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task AddCategoryAsync(Categoria category, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UnidadMedida>> ListMeasurementUnitsAsync(CancellationToken cancellationToken = default);
    Task<UnidadMedida?> GetMeasurementUnitAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ActiveSupplierExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<Producto>> ListProductsAsync(ProductListQuery query, CancellationToken cancellationToken = default);
    Task<Producto?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<Producto?> GetProductForUpdateAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<bool> HasProductHistoryAsync(Guid productId, CancellationToken cancellationToken = default);
    Task DeleteProductOwnedDataAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<PagedResult<DetalleCompra>> ListProductPurchaseCostsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<decimal?> GetLastConfirmedPurchaseCostAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<bool> ProductInternalCodeExistsAsync(string code, Guid? excludedId = null, CancellationToken cancellationToken = default);
    Task<bool> ProductBarcodeExistsAsync(string barcode, Guid? excludedId = null, CancellationToken cancellationToken = default);
    Task AddProductAsync(Producto product, CancellationToken cancellationToken = default);
}

public interface ICatalogTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
