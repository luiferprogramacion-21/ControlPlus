using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Catalog.Contracts;

public sealed record CategoryDto(
    Guid Id, string Name, string? Description, bool IsActive, long Version);

public sealed record MeasurementUnitDto(
    Guid Id, string Code, string Name, string Symbol, bool IsActive);

public sealed record ProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    Guid MeasurementUnitId,
    string MeasurementUnitCode,
    Guid? SupplierId,
    string InternalCode,
    string? Barcode,
    string? BarcodeFormat,
    bool BarcodeGenerated,
    string Name,
    string? Description,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? AverageCost,
    bool CanViewCost,
    decimal? TaxPercentage,
    int CurrentStock,
    int ReservedStock,
    int AvailableStock,
    int MinimumStock,
    bool IsOutOfStock,
    bool IsActive,
    long Version);

public sealed record CategoryListQuery(
    string? Search = null, bool IncludeInactive = false, int Page = 1, int PageSize = 20);

public sealed record ProductListQuery(
    string? Search = null,
    Guid? CategoryId = null,
    bool IncludeInactive = false,
    bool IncludeOutOfStock = false,
    int Page = 1,
    int PageSize = 20);

public sealed record ProductPurchaseCostHistoryQuery(int Page = 1, int PageSize = 20);

public sealed record PurchaseCostHistoryItemDto(
    Guid PurchaseId,
    DateOnly DocumentDate,
    DateTime ConfirmedAtUtc,
    string State,
    Guid SupplierId,
    string? SupplierDocumentNumber,
    int Quantity,
    decimal DocumentUnitCost,
    decimal? PreviousAverageCost,
    decimal ResultingAverageCost,
    decimal PreviousRetailPrice,
    decimal NewRetailPrice);

public sealed record ProductPurchaseCostHistoryDto(
    Guid ProductId,
    decimal? CurrentAverageCost,
    decimal? LastConfirmedPurchaseCost,
    PagedResult<PurchaseCostHistoryItemDto> History);

public sealed record CreateCategoryRequest(string Name, string? Description);
public sealed record UpdateCategoryRequest(string Name, string? Description, long Version);

public sealed record CreateProductRequest(
    Guid CategoryId,
    Guid MeasurementUnitId,
    Guid? SupplierId,
    string InternalCode,
    string? Barcode,
    string? BarcodeFormat,
    bool GenerateBarcode,
    string Name,
    string? Description,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? TaxPercentage,
    int MinimumStock);

public sealed record UpdateProductRequest(
    Guid CategoryId,
    Guid MeasurementUnitId,
    Guid? SupplierId,
    string InternalCode,
    string? Barcode,
    string? BarcodeFormat,
    bool BarcodeGenerated,
    string Name,
    string? Description,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? TaxPercentage,
    int MinimumStock,
    long Version);

public interface ICatalogService
{
    Task<Result<PagedResult<CategoryDto>>> ListCategoriesAsync(ActorContext actor, CategoryListQuery query, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> GetCategoryAsync(ActorContext actor, Guid categoryId, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> CreateCategoryAsync(ActorContext actor, CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> UpdateCategoryAsync(ActorContext actor, Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> SetCategoryActiveAsync(ActorContext actor, Guid categoryId, bool active, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<MeasurementUnitDto>>> ListMeasurementUnitsAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<ProductDto>>> ListProductsAsync(ActorContext actor, ProductListQuery query, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> GetProductAsync(ActorContext actor, Guid productId, CancellationToken cancellationToken = default);
    Task<Result<ProductPurchaseCostHistoryDto>> GetProductPurchaseCostHistoryAsync(ActorContext actor, Guid productId, ProductPurchaseCostHistoryQuery query, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> CreateProductAsync(ActorContext actor, CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> UpdateProductAsync(ActorContext actor, Guid productId, UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> SetProductActiveAsync(ActorContext actor, Guid productId, bool active, CancellationToken cancellationToken = default);
}
