using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Catalog.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Catalog.Services;

public sealed class CatalogService(
    ICatalogRepository repository,
    IPermissionChecker permissionChecker,
    IAuditRepository auditRepository,
    IUnitOfWork unitOfWork,
    IClock clock) : ICatalogService
{
    private static readonly IReadOnlySet<string> BarcodeFormats = new HashSet<string>(
        ["CODE128", "EAN13", "EAN8", "UPC_A", "OTRO"], StringComparer.OrdinalIgnoreCase);

    public async Task<Result<PagedResult<CategoryDto>>> ListCategoriesAsync(
        ActorContext actor, CategoryListQuery query, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CategoriesRead, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<CategoryDto>>(error);
        var paging = ValidatePaging(query.Page, query.PageSize);
        if (paging is not null) return Result.Failure<PagedResult<CategoryDto>>(paging);
        var result = await repository.ListCategoriesAsync(query, cancellationToken);
        return Result.Success(new PagedResult<CategoryDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<CategoryDto>> GetCategoryAsync(
        ActorContext actor, Guid categoryId, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CategoriesRead, cancellationToken);
        if (error is not null) return Result.Failure<CategoryDto>(error);
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        return category is null
            ? Result.Failure<CategoryDto>(ApplicationError.NotFound("la categoría"))
            : Result.Success(Map(category));
    }

    public async Task<Result<CategoryDto>> CreateCategoryAsync(
        ActorContext actor, CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CategoriesManage, cancellationToken);
        if (error is not null) return Result.Failure<CategoryDto>(error);
        var validation = ValidateCategory(request.Name, request.Description);
        if (validation is not null) return Result.Failure<CategoryDto>(validation);
        var name = request.Name.Trim();
        if (await repository.CategoryNameExistsAsync(name, null, cancellationToken))
            return Result.Failure<CategoryDto>(ApplicationError.Conflict("Ya existe una categoría con ese nombre."));

        var category = Categoria.Create(name, request.Description, actor.UserId, clock.UtcNow);
        await repository.AddCategoryAsync(category, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId, AuditAction.CategoryCreated,
            nameof(Categoria), category.Id, new { category.Nombre }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(category));
    }

    public async Task<Result<CategoryDto>> UpdateCategoryAsync(
        ActorContext actor, Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CategoriesManage, cancellationToken);
        if (error is not null) return Result.Failure<CategoryDto>(error);
        var validation = ValidateCategory(request.Name, request.Description);
        if (validation is not null) return Result.Failure<CategoryDto>(validation);
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        if (category is null) return Result.Failure<CategoryDto>(ApplicationError.NotFound("la categoría"));
        if (category.Version != request.Version) return Result.Failure<CategoryDto>(VersionConflict);
        var name = request.Name.Trim();
        if (await repository.CategoryNameExistsAsync(name, category.Id, cancellationToken))
            return Result.Failure<CategoryDto>(ApplicationError.Conflict("Ya existe una categoría con ese nombre."));

        category.Update(name, request.Description, actor.UserId, clock.UtcNow);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId, AuditAction.CategoryUpdated,
            nameof(Categoria), category.Id, new { category.Nombre, category.Version }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(category));
    }

    public async Task<Result<CategoryDto>> SetCategoryActiveAsync(
        ActorContext actor, Guid categoryId, bool active, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CategoriesManage, cancellationToken);
        if (error is not null) return Result.Failure<CategoryDto>(error);
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        if (category is null) return Result.Failure<CategoryDto>(ApplicationError.NotFound("la categoría"));
        if (!active && await repository.CategoryHasActiveProductsAsync(category.Id, cancellationToken))
            return Result.Failure<CategoryDto>(ApplicationError.Conflict("No se puede inactivar una categoría con productos activos."));

        if (active) category.Activate(actor.UserId, clock.UtcNow);
        else category.Deactivate(actor.UserId, clock.UtcNow);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
            active ? AuditAction.CategoryActivated : AuditAction.CategoryDeactivated,
            nameof(Categoria), category.Id, new { category.Nombre }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(category));
    }

    public async Task<Result<IReadOnlyCollection<MeasurementUnitDto>>> ListMeasurementUnitsAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsRead, cancellationToken);
        if (error is not null) return Result.Failure<IReadOnlyCollection<MeasurementUnitDto>>(error);
        var units = await repository.ListMeasurementUnitsAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<MeasurementUnitDto>>(units.Select(Map).ToArray());
    }

    public async Task<Result<PagedResult<ProductDto>>> ListProductsAsync(
        ActorContext actor, ProductListQuery query, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsRead, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<ProductDto>>(error);
        var paging = ValidatePaging(query.Page, query.PageSize);
        if (paging is not null) return Result.Failure<PagedResult<ProductDto>>(paging);
        var canViewCost = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken);
        var result = await repository.ListProductsAsync(query, cancellationToken);
        return Result.Success(new PagedResult<ProductDto>(result.Items.Select(x => Map(x, canViewCost)).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<ProductDto>> GetProductAsync(
        ActorContext actor, Guid productId, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsRead, cancellationToken);
        if (error is not null) return Result.Failure<ProductDto>(error);
        var product = await repository.GetProductAsync(productId, cancellationToken);
        if (product is null) return Result.Failure<ProductDto>(ApplicationError.NotFound("el producto"));
        var canViewCost = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken);
        return Result.Success(Map(product, canViewCost));
    }

    public async Task<Result<ProductPurchaseCostHistoryDto>> GetProductPurchaseCostHistoryAsync(
        ActorContext actor,
        Guid productId,
        ProductPurchaseCostHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductCostsRead, cancellationToken);
        if (error is not null) return Result.Failure<ProductPurchaseCostHistoryDto>(error);
        var paging = ValidatePaging(query.Page, query.PageSize);
        if (paging is not null) return Result.Failure<ProductPurchaseCostHistoryDto>(paging);
        var product = await repository.GetProductAsync(productId, cancellationToken);
        if (product is null) return Result.Failure<ProductPurchaseCostHistoryDto>(ApplicationError.NotFound("el producto"));

        var history = await repository.ListProductPurchaseCostsAsync(
            productId, query.Page, query.PageSize, cancellationToken);
        var lastConfirmedCost = await repository.GetLastConfirmedPurchaseCostAsync(productId, cancellationToken);
        return Result.Success(new ProductPurchaseCostHistoryDto(
            product.Id,
            product.CostoPromedio,
            lastConfirmedCost,
            new PagedResult<PurchaseCostHistoryItemDto>(
                history.Items.Select(Map).ToArray(),
                history.Page,
                history.PageSize,
                history.TotalCount)));
    }

    public async Task<Result<ProductDto>> CreateProductAsync(
        ActorContext actor, CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsCreate, cancellationToken);
        if (error is not null) return Result.Failure<ProductDto>(error);
        var validation = ValidateProduct(request.InternalCode, request.Name, request.Description, request.Barcode,
            request.BarcodeFormat, request.GenerateBarcode, request.RetailPrice, request.WholesalePrice,
            request.TaxPercentage, request.MinimumStock);
        if (validation is not null) return Result.Failure<ProductDto>(validation);
        var related = await ValidateRelationsAsync(request.CategoryId, request.MeasurementUnitId, request.SupplierId, cancellationToken);
        if (related.Error is not null) return Result.Failure<ProductDto>(related.Error);

        var internalCode = request.InternalCode.Trim().ToUpperInvariant();
        if (await repository.ProductInternalCodeExistsAsync(internalCode, null, cancellationToken))
            return Result.Failure<ProductDto>(ApplicationError.Conflict("Ya existe un producto con ese código interno."));
        var barcode = request.GenerateBarcode ? await GenerateBarcodeAsync(cancellationToken) : NormalizeOptional(request.Barcode);
        if (barcode is not null && await repository.ProductBarcodeExistsAsync(barcode, null, cancellationToken))
            return Result.Failure<ProductDto>(ApplicationError.Conflict("Ya existe un producto con ese código de barras."));
        var format = request.GenerateBarcode ? "CODE128" : NormalizeOptional(request.BarcodeFormat)?.ToUpperInvariant();

        var product = Producto.Create(request.CategoryId, request.MeasurementUnitId, request.SupplierId,
            internalCode, barcode, format, request.GenerateBarcode, request.Name.Trim(), request.Description,
            request.RetailPrice, request.WholesalePrice, request.TaxPercentage, request.MinimumStock,
            actor.UserId, clock.UtcNow);
        product.Categoria = related.Category!;
        product.UnidadMedida = related.Unit!;
        await repository.AddProductAsync(product, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId, AuditAction.ProductCreated,
            nameof(Producto), product.Id, new { product.CodigoInterno, product.Nombre, product.CategoriaId }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(product, actor.HasPermission(PermissionCodes.ProductCostsRead)));
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(
        ActorContext actor, Guid productId, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsSensitiveUpdate, cancellationToken);
        if (error is not null) return Result.Failure<ProductDto>(error);
        var validation = ValidateProduct(request.InternalCode, request.Name, request.Description, request.Barcode,
            request.BarcodeFormat, request.BarcodeGenerated, request.RetailPrice, request.WholesalePrice,
            request.TaxPercentage, request.MinimumStock);
        if (validation is not null) return Result.Failure<ProductDto>(validation);
        var product = await repository.GetProductAsync(productId, cancellationToken);
        if (product is null) return Result.Failure<ProductDto>(ApplicationError.NotFound("el producto"));
        if (product.Version != request.Version) return Result.Failure<ProductDto>(VersionConflict);
        var related = await ValidateRelationsAsync(request.CategoryId, request.MeasurementUnitId, request.SupplierId, cancellationToken);
        if (related.Error is not null) return Result.Failure<ProductDto>(related.Error);
        var internalCode = request.InternalCode.Trim().ToUpperInvariant();
        var barcode = NormalizeOptional(request.Barcode);
        if (await repository.ProductInternalCodeExistsAsync(internalCode, product.Id, cancellationToken))
            return Result.Failure<ProductDto>(ApplicationError.Conflict("Ya existe un producto con ese código interno."));
        if (barcode is not null && await repository.ProductBarcodeExistsAsync(barcode, product.Id, cancellationToken))
            return Result.Failure<ProductDto>(ApplicationError.Conflict("Ya existe un producto con ese código de barras."));

        product.Update(request.CategoryId, request.MeasurementUnitId, request.SupplierId, internalCode,
            barcode, NormalizeOptional(request.BarcodeFormat)?.ToUpperInvariant(), request.BarcodeGenerated,
            request.Name.Trim(), request.Description, request.RetailPrice, request.WholesalePrice,
            request.TaxPercentage, request.MinimumStock, actor.UserId, clock.UtcNow);
        product.Categoria = related.Category!;
        product.UnidadMedida = related.Unit!;
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId, AuditAction.ProductUpdated,
            nameof(Producto), product.Id, new { product.CodigoInterno, product.Nombre, product.PrecioMinorista, product.Version }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var canViewCost = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken);
        return Result.Success(Map(product, canViewCost));
    }

    public async Task<Result<ProductDto>> SetProductActiveAsync(
        ActorContext actor, Guid productId, bool active, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsSensitiveUpdate, cancellationToken);
        if (error is not null) return Result.Failure<ProductDto>(error);
        var product = await repository.GetProductAsync(productId, cancellationToken);
        if (product is null) return Result.Failure<ProductDto>(ApplicationError.NotFound("el producto"));
        if (active && (!product.Categoria.Activo || !product.UnidadMedida.Activo))
            return Result.Failure<ProductDto>(ApplicationError.Validation(
                "La categoría y la unidad de medida deben estar activas para reactivar el producto."));
        if (active) product.Activate(actor.UserId, clock.UtcNow);
        else product.Deactivate(actor.UserId, clock.UtcNow);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
            active ? AuditAction.ProductActivated : AuditAction.ProductDeactivated,
            nameof(Producto), product.Id, new { product.CodigoInterno }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var canViewCost = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken);
        return Result.Success(Map(product, canViewCost));
    }

    private async Task<ApplicationError?> RequireAsync(ActorContext actor, string permission, CancellationToken cancellationToken) =>
        await permissionChecker.HasPermissionAsync(actor.UserId, permission, cancellationToken)
            ? null : ApplicationError.Forbidden();

    private async Task<(Categoria? Category, UnidadMedida? Unit, ApplicationError? Error)> ValidateRelationsAsync(
        Guid categoryId, Guid unitId, Guid? supplierId, CancellationToken cancellationToken)
    {
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        if (category is null || !category.Activo) return (null, null, ApplicationError.Validation("La categoría debe existir y estar activa."));
        var unit = await repository.GetMeasurementUnitAsync(unitId, cancellationToken);
        if (unit is null || !unit.Activo) return (null, null, ApplicationError.Validation("La unidad de medida debe existir y estar activa."));
        if (supplierId is Guid id && !await repository.ActiveSupplierExistsAsync(id, cancellationToken))
            return (null, null, ApplicationError.Validation("El proveedor debe existir y estar activo."));
        return (category, unit, null);
    }

    private async Task<string> GenerateBarcodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"CP{Guid.CreateVersion7():N}"[..22].ToUpperInvariant();
            if (!await repository.ProductBarcodeExistsAsync(candidate, null, cancellationToken)) return candidate;
        }
        throw new InvalidOperationException("No se pudo generar un código de barras único.");
    }

    private static ApplicationError? ValidateCategory(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) return ApplicationError.Validation("El nombre de la categoría es obligatorio.");
        if (name.Trim().Length > 100) return ApplicationError.Validation("El nombre de la categoría no puede superar 100 caracteres.");
        if (description?.Trim().Length > 500) return ApplicationError.Validation("La descripción de la categoría no puede superar 500 caracteres.");
        return null;
    }

    private static ApplicationError? ValidateProduct(
        string? internalCode, string? name, string? description, string? barcode, string? barcodeFormat,
        bool barcodeGenerated, decimal retailPrice, decimal? wholesalePrice, decimal? taxPercentage, int minimumStock)
    {
        if (string.IsNullOrWhiteSpace(internalCode) || internalCode.Trim().Length > 50)
            return ApplicationError.Validation("El código interno es obligatorio y no puede superar 50 caracteres.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            return ApplicationError.Validation("El nombre es obligatorio y no puede superar 200 caracteres.");
        if (retailPrice < 0 || retailPrice != decimal.Truncate(retailPrice))
            return ApplicationError.Validation("El precio minorista debe ser un valor entero mayor o igual a cero.");
        if (wholesalePrice is < 0 || wholesalePrice is decimal value && value != decimal.Truncate(value))
            return ApplicationError.Validation("El precio mayorista debe ser nulo o un valor entero mayor o igual a cero.");
        if (taxPercentage is < 0 or > 100) return ApplicationError.Validation("El IVA debe estar entre 0 y 100.");
        if (minimumStock < 0) return ApplicationError.Validation("El stock mínimo no puede ser negativo.");
        var normalizedBarcode = NormalizeOptional(barcode);
        var normalizedFormat = NormalizeOptional(barcodeFormat)?.ToUpperInvariant();
        if (!(barcodeGenerated && normalizedBarcode is null))
        {
            if (normalizedBarcode?.Length > 100) return ApplicationError.Validation("El código de barras no puede superar 100 caracteres.");
            if ((normalizedBarcode is null) != (normalizedFormat is null))
                return ApplicationError.Validation("El código de barras y su formato deben informarse juntos.");
            if (normalizedFormat is not null && !BarcodeFormats.Contains(normalizedFormat))
                return ApplicationError.Validation("El formato de código de barras no es válido.");
        }
        return null;
    }

    private static ApplicationError? ValidatePaging(int page, int pageSize) =>
        page < 1 || pageSize is < 1 or > 200
            ? ApplicationError.Validation("La página debe ser mayor que cero y el tamaño debe estar entre 1 y 200.")
            : null;

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static CategoryDto Map(Categoria x) => new(x.Id, x.Nombre, x.Descripcion, x.Activo, x.Version);
    private static MeasurementUnitDto Map(UnidadMedida x) => new(x.Id, x.Codigo, x.Nombre, x.Simbolo, x.Activo);
    private static ProductDto Map(Producto x, bool canViewCost) => new(
        x.Id, x.CategoriaId, x.Categoria.Nombre, x.UnidadMedidaId, x.UnidadMedida.Codigo, x.ProveedorId,
        x.CodigoInterno, x.CodigoBarras, x.FormatoCodigoBarras, x.CodigoBarrasGenerado, x.Nombre, x.Descripcion,
        x.PrecioMinorista, x.PrecioMayorista, canViewCost ? x.CostoPromedio : null, canViewCost,
        x.PorcentajeIva, x.StockActual, x.StockReservado, x.StockDisponible, x.StockMinimo, x.Agotado, x.Activo, x.Version);
    private static PurchaseCostHistoryItemDto Map(DetalleCompra x) => new(
        x.CompraId,
        x.Compra.FechaDocumento,
        x.Compra.FechaHoraConfirmacion!.Value,
        x.Compra.Estado,
        x.Compra.ProveedorId,
        x.Compra.NumeroDocumentoProveedor,
        x.Cantidad,
        x.CostoUnitarioDocumento,
        x.CostoPromedioAnterior,
        x.CostoPromedioResultante,
        x.PrecioVentaAnterior,
        x.PrecioVentaNuevo);

    private static readonly ApplicationError VersionConflict =
        ApplicationError.Conflict("El registro fue modificado por otro usuario. Recargue los datos e intente nuevamente.");
}
