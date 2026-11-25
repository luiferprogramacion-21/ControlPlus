using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Catalog.Contracts;

public sealed record CategoryDto(
    Guid Id, string Name, string? Description, bool IsActive, long Version, string? Abbreviation = null);

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
    long Version)
{
    public ProductDto RestrictCostVisibility(bool canViewCost) => this with
    {
        AverageCost = CanViewCost && canViewCost ? AverageCost : null,
        CanViewCost = CanViewCost && canViewCost
    };
}

public sealed record CategoryListQuery(
    string? Search = null, bool IncludeInactive = false, int Page = 1, int PageSize = 20);

public sealed record ProductListQuery(
    string? Search = null,
    Guid? CategoryId = null,
    bool IncludeInactive = false,
    bool IncludeOutOfStock = false,
    int Page = 1,
    int PageSize = 20,
    // Auditoría del 23/11/2026 (C.19): DISPONIBLE, STOCK_BAJO o AGOTADO con el mismo criterio de Inventario y del Dashboard
    // (InventoryStockStatus). Lo usa también el Cajero; con estado no se aplica IncludeOutOfStock.
    string? StockStatus = null);

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

public sealed record CreateCategoryRequest(string Name, string? Description, string? Abbreviation = null);
public sealed record UpdateCategoryRequest(string Name, string? Description, long Version, string? Abbreviation = null);

public sealed record CreateProductRequest(
    Guid CategoryId,
    Guid MeasurementUnitId,
    Guid? SupplierId,
    string? InternalCode,
    string? Barcode,
    string? BarcodeFormat,
    bool GenerateBarcode,
    string Name,
    string? Description,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? TaxPercentage,
    int MinimumStock,
    int InitialQuantity = 0,
    decimal? InitialUnitCost = null);

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
    long Version,
    ProductAuthorizationRequest? Authorization = null);

/// <summary>
/// Decisión D5 del 23/11/2026: el Supervisor cambia el precio y los datos de un producto con la autorización del
/// Administrador (usuario y contraseña, o su carné escaneado). Quien tiene PRODUCTS.SENSITIVE_UPDATE no la envía.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductAuthorizationRequest(string? UserName, string? Password, string? CredentialToken = null)
    : IAuthorizerCredentials
{
    [System.Text.Json.Serialization.JsonIgnore] public Guid? ResolvedCredentialId { get; init; }
}

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
    Task<Result<ProductDto>> CreateProductAsync(ActorContext actor, CreateProductRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> UpdateProductAsync(ActorContext actor, Guid productId, UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> SetProductActiveAsync(ActorContext actor, Guid productId, bool active, CancellationToken cancellationToken = default);
    Task<Result> DeleteProductAsync(ActorContext actor, Guid productId, long expectedVersion, CancellationToken cancellationToken = default);
}
