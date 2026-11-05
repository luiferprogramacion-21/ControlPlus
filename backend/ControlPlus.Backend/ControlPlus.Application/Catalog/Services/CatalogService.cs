using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Catalog.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Purchases;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Catalog.Services;

public sealed class CatalogService(
    ICatalogRepository repository,
    IPermissionChecker permissionChecker,
    IUserRepository userRepository,
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
        if (InvalidAbbreviation(request.Abbreviation))
            return Result.Failure<CategoryDto>(ApplicationError.Validation("La abreviatura debe tener de 2 a 10 letras ASCII."));
        var name = request.Name.Trim();
        if (await repository.CategoryNameExistsAsync(name, null, cancellationToken))
            return Result.Failure<CategoryDto>(ApplicationError.Conflict("Ya existe una categoría con ese nombre."));

        var category = Categoria.Create(name, request.Description, actor.UserId, clock.UtcNow);
        if (string.IsNullOrWhiteSpace(request.Abbreviation))
            return Result.Failure<CategoryDto>(ApplicationError.Validation("La abreviatura es obligatoria."));
        var abbreviation = Categoria.NormalizeAbbreviation(request.Abbreviation);
        if (await repository.CategoryAbbreviationExistsAsync(abbreviation, null, cancellationToken))
            return Result.Failure<CategoryDto>(ApplicationError.Conflict("La abreviatura ya está asignada."));
        category.SetAbbreviation(abbreviation);
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
        if (request.Abbreviation is not null && InvalidAbbreviation(request.Abbreviation))
            return Result.Failure<CategoryDto>(ApplicationError.Validation("La abreviatura debe tener de 2 a 10 letras ASCII."));
        var category = await repository.GetCategoryAsync(categoryId, cancellationToken);
        if (category is null) return Result.Failure<CategoryDto>(ApplicationError.NotFound("la categoría"));
        if (category.Version != request.Version) return Result.Failure<CategoryDto>(VersionConflict);
        var name = request.Name.Trim();
        if (await repository.CategoryNameExistsAsync(name, category.Id, cancellationToken))
            return Result.Failure<CategoryDto>(ApplicationError.Conflict("Ya existe una categoría con ese nombre."));

        if (!string.IsNullOrWhiteSpace(request.Abbreviation))
        {
            var abbreviation = Categoria.NormalizeAbbreviation(request.Abbreviation);
            if (await repository.CategoryAbbreviationExistsAsync(abbreviation, category.Id, cancellationToken))
                return Result.Failure<CategoryDto>(ApplicationError.Conflict("La abreviatura ya está asignada."));
            category.SetAbbreviation(abbreviation);
        }

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

        if (query.IncludeOutOfStock &&
            !await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.InventoryRead, cancellationToken))
        {
            return Result.Failure<PagedResult<ProductDto>>(ApplicationError.Forbidden(
                "El listado completo de productos agotados requiere INVENTORY.READ."));
        }

        if (query.IncludeInactive &&
            !await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductsSensitiveUpdate, cancellationToken))
        {
            return Result.Failure<PagedResult<ProductDto>>(ApplicationError.Forbidden(
                "El listado de productos inactivos requiere PRODUCTS.SENSITIVE_UPDATE."));
        }

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
        ActorContext actor, CreateProductRequest request, CancellationToken cancellationToken = default,
        string? idempotencyKey = null)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey is not null && (key is null || key.Length > 100))
            return Result.Failure<ProductDto>(ApplicationError.Validation("Idempotency-Key debe tener entre 1 y 100 caracteres."));
        if (request.InitialQuantity < 0)
            return Result.Failure<ProductDto>(ApplicationError.Validation("La existencia inicial no puede ser negativa."));
        if (request.InitialQuantity > 0 && key is null)
            return Result.Failure<ProductDto>(ApplicationError.Validation("Idempotency-Key es obligatorio para existencias iniciales."));
        if (request.InitialQuantity > 0 && request.InitialUnitCost is null)
            return Result.Failure<ProductDto>(ApplicationError.Validation("El costo inicial es obligatorio cuando hay existencias."));
        if (request.InitialQuantity == 0 && request.InitialUnitCost is not null)
            return Result.Failure<ProductDto>(ApplicationError.Validation("El costo inicial requiere existencias positivas."));
        if (request.InitialUnitCost is decimal cost &&
            (cost < 0 || cost > PurchaseCostPolicy.MaximumFourPlaceAmount || cost != decimal.Round(cost, 4)))
            return Result.Failure<ProductDto>(ApplicationError.Validation("El costo inicial debe ser no negativo y tener hasta cuatro decimales dentro de numeric(18,4)."));

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        Instalacion? installation = null;
        Terminal? terminal = null;
        string? requestHash = null;
        if (key is not null)
        {
            installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
            if (installation is null) return Result.Failure<ProductDto>(ApplicationError.NotFound("la instalación activa"));
        }
        var error = await RequireAsync(actor, PermissionCodes.ProductsCreate, cancellationToken);
        if (error is not null) return Result.Failure<ProductDto>(error);
        if (request.InitialQuantity > 0)
        {
            var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
            if (authorization is null || !authorization.Roles.Any(role =>
                    role.Code is RoleCodes.Administrator or RoleCodes.Supervisor))
                return Result.Failure<ProductDto>(ApplicationError.Forbidden());
        }
        if (key is not null)
        {
            requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
            var existing = await repository.GetIdempotencyAsync(installation!.Id, key, cancellationToken);
            if (existing is not null)
            {
                if (existing.UsuarioId != actor.UserId || existing.TipoOperacion != "CREAR_PRODUCTO" ||
                    existing.HashSolicitud != requestHash || existing.Entidad != nameof(Producto) ||
                    existing.EntidadId is null || existing.Resultado is null)
                    return Result.Failure<ProductDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
                var saved = JsonSerializer.Deserialize<ProductDto>(existing.Resultado)!;
                var canViewCost = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken);
                return Result.Success(saved.RestrictCostVisibility(canViewCost));
            }
            terminal = await repository.GetActiveTerminalAsync(installation.Id, cancellationToken);
            if (terminal is null) return Result.Failure<ProductDto>(ApplicationError.NotFound("la terminal activa"));
        }
        var requestedInternalCode = NormalizeOptional(request.InternalCode)?.ToUpperInvariant();
        var validation = ValidateProduct(requestedInternalCode, request.Name, request.Description, request.Barcode,
            request.BarcodeFormat, request.GenerateBarcode, request.RetailPrice, request.WholesalePrice,
            request.TaxPercentage, request.MinimumStock, allowGeneratedBarcodeWithoutValue: true,
            allowMissingInternalCode: true);
        if (validation is not null) return Result.Failure<ProductDto>(validation);
        var related = await ValidateRelationsAsync(request.CategoryId, request.MeasurementUnitId, request.SupplierId, cancellationToken);
        if (related.Error is not null) return Result.Failure<ProductDto>(related.Error);
        if (string.IsNullOrWhiteSpace(related.Category!.Abreviatura))
            return Result.Failure<ProductDto>(ApplicationError.Validation("Configure la abreviatura de la categoría antes de crear productos."));

        if (requestedInternalCode is not null &&
            await repository.ProductInternalCodeExistsAsync(requestedInternalCode, null, cancellationToken))
            return Result.Failure<ProductDto>(ApplicationError.Conflict("Ya existe un producto con ese código interno."));
        var internalCode = await repository.AllocateProductReferenceAsync(request.CategoryId, cancellationToken);
        var barcode = request.GenerateBarcode ? ProductCode.GenerateOwnedBarcode(internalCode) : NormalizeOptional(request.Barcode);
        if (barcode is not null && await repository.ProductBarcodeExistsAsync(barcode, null, cancellationToken))
            return Result.Failure<ProductDto>(ApplicationError.Conflict("El código ya identifica otro producto."));
        var format = request.GenerateBarcode ? "CODE128" : NormalizeOptional(request.BarcodeFormat)?.ToUpperInvariant();
        var productId = Guid.CreateVersion7();

        var product = Producto.Create(request.CategoryId, request.MeasurementUnitId, request.SupplierId,
            internalCode, barcode, format, request.GenerateBarcode, request.Name.Trim(), request.Description,
            request.RetailPrice, request.WholesalePrice, request.TaxPercentage, request.MinimumStock,
            actor.UserId, clock.UtcNow, productId);
        product.Categoria = related.Category!;
        product.UnidadMedida = related.Unit!;
        MovimientoInventario? movement = null;
        var correlationId = Guid.CreateVersion7();
        if (request.InitialQuantity > 0)
        {
            product.StockActual = request.InitialQuantity;
            product.CostoPromedio = request.InitialUnitCost;
            movement = new MovimientoInventario
            {
                Id = Guid.CreateVersion7(), ProductoId = product.Id, Producto = product,
                UsuarioId = actor.UserId, TipoMovimiento = "INVENTARIO_INICIAL",
                CantidadStock = request.InitialQuantity, CantidadReservada = 0,
                StockAnterior = 0, StockPosterior = request.InitialQuantity,
                ReservadoAnterior = 0, ReservadoPosterior = 0,
                CostoPromedioAnterior = null, CostoPromedioPosterior = request.InitialUnitCost,
                FechaHora = clock.UtcNow.UtcDateTime, CorrelacionId = correlationId
            };
            await repository.AddMovementAsync(movement, cancellationToken);
        }
        await repository.AddProductAsync(product, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId, AuditAction.ProductCreated,
            nameof(Producto), product.Id, new { product.CodigoInterno, product.Nombre, product.CategoriaId,
                InitialQuantity = request.InitialQuantity, MovementId = movement?.Id },
            cancellationToken, correlationId, terminal?.Id);
        var canSeeCost = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken);
        if (key is not null)
        {
            var operation = new OperacionIdempotente
            {
                Id = Guid.CreateVersion7(), InstalacionId = installation!.Id, TerminalId = terminal!.Id,
                UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "CREAR_PRODUCTO",
                HashSolicitud = requestHash!, Entidad = nameof(Producto), EntidadId = product.Id,
                CorrelacionId = correlationId, FechaRegistro = clock.UtcNow.UtcDateTime,
                Resultado = JsonSerializer.Serialize(Map(product, canSeeCost))
            };
            await repository.AddIdempotencyAsync(operation, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (requestedInternalCode is not null)
            await repository.AddProductCodeAliasAsync(product.Id, requestedInternalCode, cancellationToken);
        var response = Map(product, canSeeCost);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(
        ActorContext actor, Guid productId, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ProductsSensitiveUpdate, cancellationToken);
        if (error is not null) return Result.Failure<ProductDto>(error);
        var validation = ValidateProduct(request.InternalCode, request.Name, request.Description, request.Barcode,
            request.BarcodeFormat, request.BarcodeGenerated, request.RetailPrice, request.WholesalePrice,
            request.TaxPercentage, request.MinimumStock, allowGeneratedBarcodeWithoutValue: false);
        if (validation is not null) return Result.Failure<ProductDto>(validation);
        var product = await repository.GetProductAsync(productId, cancellationToken);
        if (product is null) return Result.Failure<ProductDto>(ApplicationError.NotFound("el producto"));
        if (product.Version != request.Version) return Result.Failure<ProductDto>(VersionConflict);
        if (!string.Equals(product.CodigoInterno, request.InternalCode.Trim(), StringComparison.Ordinal))
            return Result.Failure<ProductDto>(ApplicationError.Validation("El N.º de producto es permanente y no puede cambiarse."));
        if (request.BarcodeGenerated != product.CodigoBarrasGenerado ||
            (product.CodigoBarrasGenerado && (request.Barcode != product.CodigoBarras || request.BarcodeFormat != product.FormatoCodigoBarras)))
            return Result.Failure<ProductDto>(ApplicationError.Validation("La edición debe conservar el código generado y sus metadatos."));
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

    private static ApplicationError? ValidateCategory(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) return ApplicationError.Validation("El nombre de la categoría es obligatorio.");
        if (name.Trim().Length > 100) return ApplicationError.Validation("El nombre de la categoría no puede superar 100 caracteres.");
        if (description?.Trim().Length > 500) return ApplicationError.Validation("La descripción de la categoría no puede superar 500 caracteres.");
        return null;
    }

    private static bool InvalidAbbreviation(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is null || normalized.Length is < 2 or > 10 ||
            normalized.Any(c => c is < 'A' or > 'Z');
    }

    private static ApplicationError? ValidateProduct(
        string? internalCode, string? name, string? description, string? barcode, string? barcodeFormat,
        bool barcodeGenerated, decimal retailPrice, decimal? wholesalePrice, decimal? taxPercentage, int minimumStock,
        bool allowGeneratedBarcodeWithoutValue, bool allowMissingInternalCode = false)
    {
        if ((!allowMissingInternalCode && string.IsNullOrWhiteSpace(internalCode)) || internalCode?.Trim().Length > 50)
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
        if (barcodeGenerated && normalizedBarcode is null && !allowGeneratedBarcodeWithoutValue)
            return ApplicationError.Validation("Un código de barras marcado como generado requiere el código y un formato válido.");
        if (!(allowGeneratedBarcodeWithoutValue && barcodeGenerated && normalizedBarcode is null))
        {
            if (normalizedBarcode?.Length > 100) return ApplicationError.Validation("El código de barras no puede superar 100 caracteres.");
            if (normalizedBarcode is null && normalizedFormat is not null ||
                barcodeGenerated && normalizedBarcode is not null && normalizedFormat is null)
                return ApplicationError.Validation("Un código generado requiere formato; un código externo puede tener formato desconocido.");
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
    private static CategoryDto Map(Categoria x) => new(x.Id, x.Nombre, x.Descripcion, x.Activo, x.Version, x.Abreviatura);
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
        ApplicationError.ConcurrencyConflict("El registro fue modificado por otro usuario. Recargue los datos e intente nuevamente.");
}
