using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Purchases.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Purchases;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Purchases.Services;

public sealed class PurchaseService(IPurchaseRepository repository, IUserRepository users,
    IAuditRepository audit, IUnitOfWork unitOfWork, IClock clock) : IPurchaseService
{
    private const string InternalDocumentPrefix = "CP-INTERNO-";

    public async Task<Result<PagedResult<PurchaseSummaryDto>>> ListAsync(ActorContext actor,
        PurchaseListQuery query, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor, ct)) return Denied<PagedResult<PurchaseSummaryDto>>();
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            return Invalid<PagedResult<PurchaseSummaryDto>>("La página debe ser positiva y pageSize debe estar entre 1 y 100.");
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue) return Invalid<PagedResult<PurchaseSummaryDto>>("La página solicitada supera el límite permitido.");
        if (query.SupplierId == Guid.Empty || query.InvoiceDate == DateOnly.MinValue)
            return Invalid<PagedResult<PurchaseSummaryDto>>("Los filtros de proveedor o fecha son inválidos.");
        var status = query.Status?.Trim().ToUpperInvariant();
        if (status is not null and not ("BORRADOR" or "CONFIRMADA" or "ANULADA"))
            return Invalid<PagedResult<PurchaseSummaryDto>>("El estado solicitado es inválido.");
        var document = NormalizeDocument(query.SupplierDocumentNumber);
        if (document?.Length > 100) return Invalid<PagedResult<PurchaseSummaryDto>>("El filtro de documento supera 100 caracteres.");
        var result = await repository.ListAsync(query with { Status = status, SupplierDocumentNumber = document }, (int)offset, ct);
        return Result.Success(new PagedResult<PurchaseSummaryDto>(result.Items.Select(MapSummary).ToArray(),
            result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<PurchaseDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        if (id == Guid.Empty) return Invalid<PurchaseDto>("purchaseId debe ser un UUID válido.");
        var purchase = await repository.GetAsync(id, ct);
        return purchase is null ? Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra")) : Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseDto>> CreateAsync(ActorContext actor, CreatePurchaseRequest request,
        CancellationToken ct = default)
    {
        var input = Validate(request.SupplierId, request.SupplierDocumentNumber, request.InvoiceDate,
            request.VatMode, request.Lines);
        if (input.Error is not null) return Result.Failure<PurchaseDto>(input.Error);
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        var supplier = await repository.GetSupplierForUpdateAsync(request.SupplierId, ct);
        if (supplier is null || !supplier.Activo) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el proveedor activo"));
        if (input.Document is not null && await repository.DocumentExistsAsync(supplier.Id, input.Document, null, ct))
            return DuplicateDocument();
        var products = await repository.GetProductsForUpdateAsync(input.Lines!.Select(x => x.ProductId).ToArray(), ct);
        if (products.Count != input.Lines!.Count || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseDto>(ApplicationError.NotFound("uno o más productos activos"));
        var prepared = Prepare(input.Lines, input.VatMode!, products);
        if (prepared.Error is not null) return Result.Failure<PurchaseDto>(prepared.Error);
        var now = clock.UtcNow.UtcDateTime;
        var purchase = new Compra
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, ProveedorId = supplier.Id,
            PedidoCompraId = null, UsuarioId = actor.UserId, NumeroDocumentoProveedor = input.Document,
            FechaDocumento = request.InvoiceDate, FechaHoraRegistro = now, ModoIva = input.VatMode!,
            SubtotalBruto = prepared.Gross, ImpuestoTotal = prepared.Vat, Total = prepared.Total,
            Estado = "BORRADOR", Version = 1
        };
        foreach (var line in prepared.Lines!) purchase.DetalleCompra.Add(ToDetail(purchase.Id, line));
        await repository.AddAsync(purchase, ct);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseDraftCreated,
            nameof(Compra), purchase.Id,
            new { PurchaseId = purchase.Id, State = purchase.Estado, purchase.Version,
                ModifiedFields = new[] { "supplierId", "supplierDocumentNumber", "invoiceDate", "vatMode", "lines" },
                CorrelationId = correlationId }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseDto>> UpdateAsync(ActorContext actor, Guid id,
        UpdatePurchaseRequest request, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return Invalid<PurchaseDto>("purchaseId debe ser un UUID válido.");
        if (request.ExpectedVersion <= 0) return Invalid<PurchaseDto>("expectedVersion debe ser positivo.");
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.ConcurrencyConflict("La versión de la compra está agotada."));
        var input = Validate(request.SupplierId, request.SupplierDocumentNumber, request.InvoiceDate,
            request.VatMode, request.Lines);
        if (input.Error is not null) return Result.Failure<PurchaseDto>(input.Error);
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        var purchase = await repository.GetForUpdateAsync(id, ct);
        if (purchase is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra"));
        if (purchase.Version != request.ExpectedVersion) return Stale();
        if (purchase.Estado != "BORRADOR") return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Solo se puede editar una compra BORRADOR."));
        var supplier = await repository.GetSupplierForUpdateAsync(request.SupplierId, ct);
        if (supplier is null || !supplier.Activo) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el proveedor activo"));
        if (input.Document is not null && await repository.DocumentExistsAsync(supplier.Id, input.Document, id, ct))
            return DuplicateDocument();
        var products = await repository.GetProductsForUpdateAsync(input.Lines!.Select(x => x.ProductId).ToArray(), ct);
        if (products.Count != input.Lines!.Count || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseDto>(ApplicationError.NotFound("uno o más productos activos"));
        var prepared = Prepare(input.Lines, input.VatMode!, products);
        if (prepared.Error is not null) return Result.Failure<PurchaseDto>(prepared.Error);
        purchase.ProveedorId = supplier.Id;
        purchase.NumeroDocumentoProveedor = input.Document;
        purchase.FechaDocumento = request.InvoiceDate;
        purchase.ModoIva = input.VatMode!;
        purchase.SubtotalBruto = prepared.Gross;
        purchase.ImpuestoTotal = prepared.Vat;
        purchase.Total = prepared.Total;
        purchase.Version = checked(request.ExpectedVersion + 1);
        await repository.ReplaceDetailsAsync(purchase, prepared.Lines!.Select(x => ToDetail(id, x)).ToArray(), ct);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseDraftUpdated,
            nameof(Compra), purchase.Id,
            new { PurchaseId = purchase.Id, State = purchase.Estado, Version = purchase.Version,
                ModifiedFields = new[] { "supplierId", "supplierDocumentNumber", "invoiceDate", "vatMode", "lines" },
                CorrelationId = correlationId }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseDto>> ConfirmAsync(ActorContext actor, Guid id, string key,
        ConfirmPurchaseRequest request, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return Invalid<PurchaseDto>("purchaseId debe ser un UUID válido.");
        if (request.ExpectedVersion <= 0) return Invalid<PurchaseDto>("expectedVersion debe ser positivo.");
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.ConcurrencyConflict("La versión de la compra está agotada."));
        key = key?.Trim() ?? "";
        if (key.Length is < 1 or > 100 || key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')))
            return Invalid<PurchaseDto>("Idempotency-Key debe tener 1 a 100 caracteres ASCII alfanuméricos o - _ . :.");
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}|{request.ExpectedVersion}")));
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "CONFIRMAR_COMPRA" ||
                previous.HashSolicitud != requestHash || previous.EntidadId != id)
                return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var original = await repository.GetAsync(id, ct);
            return original is null ? Result.Failure<PurchaseDto>(ApplicationError.Conflict("La confirmación original no es recuperable."))
                : Result.Success(Map(original));
        }
        var purchase = await repository.GetForUpdateAsync(id, ct);
        if (purchase is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra"));
        if (purchase.Version != request.ExpectedVersion) return Stale();
        if (purchase.Estado != "BORRADOR") return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La compra ya fue confirmada o no es un borrador."));
        if (purchase.DetalleCompra.Count == 0) return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La compra no contiene líneas."));
        var supplier = await repository.GetSupplierForUpdateAsync(purchase.ProveedorId, ct);
        if (supplier is null || !supplier.Activo) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el proveedor activo"));
        var sequence = await repository.GetSequenceForUpdateAsync(installation.Id, installation.Serie, ct);
        if (sequence is null) return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Debe configurar el consecutivo COMPRA antes de confirmar."));
        if (sequence.Prefijo != "CMP" || sequence.UltimoNumero < 0)
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El consecutivo COMPRA no conserva la configuración aprobada."));
        if (sequence.UltimoNumero == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.NumberingExhausted("El consecutivo COMPRA está agotado."));
        var lineIds = purchase.DetalleCompra.Select(x => x.ProductoId).ToArray();
        if (lineIds.Distinct().Count() != lineIds.Length)
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La compra contiene productos repetidos."));
        var products = await repository.GetProductsForUpdateAsync(lineIds, ct);
        if (products.Count != lineIds.Length || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseDto>(ApplicationError.NotFound("uno o más productos activos"));
        var lines = purchase.DetalleCompra.Select(x => new PurchaseLineRequest(x.ProductoId, x.Cantidad,
            x.CostoUnitarioDocumento, x.PorcentajeIva)).ToArray();
        if (Validate(purchase.ProveedorId, purchase.NumeroDocumentoProveedor, purchase.FechaDocumento,
                purchase.ModoIva, lines).Error is not null ||
            purchase.DetalleCompra.Any(x => x.ModoIva != purchase.ModoIva))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El borrador contiene cantidades, costos, documento o IVA inválidos."));
        var prepared = Prepare(lines, purchase.ModoIva, products);
        if (prepared.Error is not null || prepared.Lines is null ||
            prepared.Gross != purchase.SubtotalBruto || prepared.Vat != purchase.ImpuestoTotal || prepared.Total != purchase.Total ||
            purchase.DetalleCompra.Any(d => prepared.Lines.All(x => x.Product.Id != d.ProductoId ||
                x.Gross != d.SubtotalBruto || x.TaxBase != d.BaseGravable || x.Vat != d.ValorIva || x.Total != d.TotalLinea)))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El borrador ya no cumple el costo o IVA oficial."));
        var number = checked(sequence.UltimoNumero + 1);
        var externalDocument = NormalizeDocument(purchase.NumeroDocumentoProveedor);
        var document = externalDocument ?? $"{InternalDocumentPrefix}{installation.Id:N}-{sequence.Prefijo}-{sequence.Serie}-{number}";
        if (document.Length > 100 || await repository.DocumentExistsAsync(supplier.Id, document, purchase.Id, ct))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El documento de proveedor ya existe o supera el límite permitido."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la terminal activa"));
        var now = clock.UtcNow.UtcDateTime;
        var correlationId = Guid.CreateVersion7();
        var byId = prepared.Lines.ToDictionary(x => x.Product.Id);
        var movements = new List<MovimientoInventario>(lineIds.Length);
        try
        {
            foreach (var detail in purchase.DetalleCompra.OrderBy(x => x.ProductoId))
            {
                var line = byId[detail.ProductoId];
                var product = line.Product;
                var previousStock = product.StockActual;
                var previousAverage = product.CostoPromedio;
                var effective = PurchaseCostPolicy.EffectiveUnitCost(detail.CostoUnitarioDocumento,
                    purchase.ModoIva, detail.PorcentajeIva);
                var nextStock = checked(previousStock + detail.Cantidad);
                var average = PurchaseCostPolicy.Average(previousAverage, previousStock, detail.Cantidad, effective);
                detail.CostoPromedioAnterior = previousAverage;
                detail.CostoPromedioResultante = average;
                detail.PrecioVentaAnterior = product.PrecioMinorista;
                detail.PrecioVentaNuevo = product.PrecioMinorista;
                product.StockActual = nextStock;
                product.CostoPromedio = average;
                movements.Add(new MovimientoInventario
                {
                    Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actor.UserId,
                    CompraId = purchase.Id, TipoMovimiento = "COMPRA", CantidadStock = detail.Cantidad,
                    CantidadReservada = 0, StockAnterior = previousStock, StockPosterior = nextStock,
                    ReservadoAnterior = product.StockReservado, ReservadoPosterior = product.StockReservado,
                    CostoPromedioAnterior = previousAverage, CostoPromedioPosterior = average,
                    FechaHora = now, CorrelacionId = correlationId
                });
            }
        }
        catch (OverflowException)
        {
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El stock o costo resultante excede el rango oficial."));
        }
        // El trigger oficial de detalle_compra permite actualizar los costos solo mientras
        // la cabecera sigue en BORRADOR. Este flush permanece dentro de la misma transaccion.
        await unitOfWork.SaveChangesAsync(ct);
        sequence.UltimoNumero = number;
        purchase.Prefijo = sequence.Prefijo;
        purchase.Serie = sequence.Serie;
        purchase.Consecutivo = number;
        purchase.NumeroDocumentoProveedor = document;
        purchase.Estado = "CONFIRMADA";
        purchase.FechaHoraConfirmacion = now;
        purchase.Version = checked(request.ExpectedVersion + 1);
        await repository.AddMovementsAsync(movements, ct);
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "CONFIRMAR_COMPRA",
            HashSolicitud = requestHash, Entidad = nameof(Compra), EntidadId = purchase.Id,
            Resultado = JsonSerializer.Serialize(new { State = "CONFIRMADA", PurchaseId = purchase.Id }),
            CorrelacionId = correlationId, FechaRegistro = now
        }, ct);
        await AuditWriter.WriteCashAsync(audit, clock, actor.UserId, AuditAction.PurchaseConfirmed,
            nameof(Compra), purchase.Id,
            new { PurchaseId = purchase.Id, State = purchase.Estado, Version = purchase.Version,
                ModifiedFields = new[] { "status", "prefix", "series", "consecutive", "supplierDocumentNumber", "stock", "averageCost" },
                ProductIds = lineIds.Order().ToArray(), CorrelationId = correlationId },
            correlationId, null, null, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(purchase));
    }

    private async Task<bool> AllowedAsync(ActorContext actor, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        return snapshot is not null && snapshot.HighestRoleLevel >= RoleLevel.Supervisor &&
            snapshot.PermissionCodes.Contains(PermissionCodes.PurchasesReceive, StringComparer.OrdinalIgnoreCase);
    }

    private static ValidatedInput Validate(Guid supplierId, string? document, DateOnly date,
        string? vatMode, IReadOnlyCollection<PurchaseLineRequest>? lines)
    {
        if (supplierId == Guid.Empty) return ValidatedInput.Fail("supplierId debe ser un UUID válido.");
        if (date == DateOnly.MinValue) return ValidatedInput.Fail("invoiceDate es obligatoria y debe ser válida.");
        vatMode = vatMode?.Trim().ToUpperInvariant();
        if (vatMode is not ("NO_DISCRIMINADO" or "INCLUIDO" or "SEPARADO"))
            return ValidatedInput.Fail("vatMode debe ser NO_DISCRIMINADO, INCLUIDO o SEPARADO.");
        document = NormalizeDocument(document);
        if (document?.Length > 100 || document?.StartsWith(InternalDocumentPrefix, StringComparison.Ordinal) == true)
            return ValidatedInput.Fail("El documento de proveedor es inválido o usa el prefijo interno reservado.");
        if (lines is null || lines.Count is < 1 or > 200 || lines.Any(x => x is null))
            return ValidatedInput.Fail("La compra debe incluir entre 1 y 200 líneas válidas.");
        if (lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            return ValidatedInput.Fail("No se permite repetir un producto en la compra.");
        foreach (var line in lines)
        {
            if (line.ProductId == Guid.Empty || line.Quantity <= 0 || line.UnitCost <= 0 ||
                line.UnitCost > PurchaseCostPolicy.MaximumFourPlaceAmount ||
                decimal.Round(line.UnitCost, 4) != line.UnitCost ||
                (line.VatPercentage is decimal rate &&
                 (rate is < 0m or > 100m || decimal.Round(rate, 2) != rate)))
                return ValidatedInput.Fail("Cada línea requiere producto, cantidad entera positiva y costo/IVA dentro de la precisión oficial.");
            if (vatMode == "NO_DISCRIMINADO" && line.VatPercentage is not null)
                return ValidatedInput.Fail("NO_DISCRIMINADO no admite vatPercentage.");
        }
        return new ValidatedInput(document, vatMode, lines.ToArray(), null);
    }

    private static PreparedPurchase Prepare(IReadOnlyCollection<PurchaseLineRequest> requests, string mode,
        IReadOnlyCollection<Producto> products)
    {
        var byId = products.ToDictionary(x => x.Id);
        var lines = new List<PreparedLine>(requests.Count);
        try
        {
            foreach (var request in requests.OrderBy(x => x.ProductId))
            {
                var product = byId[request.ProductId];
                if (!product.Activo || product.PrecioMinorista <= 0 || product.StockActual < 0 ||
                    product.StockReservado < 0 || product.StockReservado > product.StockActual)
                    return PreparedPurchase.Fail("El producto no conserva un estado de inventario o precio válido.");
                var rate = mode == "NO_DISCRIMINADO" ? null : product.PorcentajeIva;
                if (mode != "NO_DISCRIMINADO" &&
                    (rate is null || rate is < 0m or > 100m || decimal.Round(rate.Value, 2) != rate.Value ||
                     request.VatPercentage is decimal supplied && supplied != rate.Value))
                    return PreparedPurchase.Fail("El IVA de la línea debe coincidir con la tasa oficial del producto.");
                var gross = PurchaseCostPolicy.CheckedFour(checked(request.UnitCost * request.Quantity));
                decimal? baseAmount = null;
                decimal? tax = null;
                var total = gross;
                if (mode == "SEPARADO")
                {
                    baseAmount = gross;
                    tax = PurchaseCostPolicy.CheckedFour(PurchaseCostPolicy.Round4(gross * rate!.Value / 100m));
                    total = PurchaseCostPolicy.CheckedFour(checked(gross + tax.Value));
                }
                else if (mode == "INCLUIDO")
                {
                    baseAmount = PurchaseCostPolicy.CheckedFour(PurchaseCostPolicy.Round4(gross * 100m / (100m + rate!.Value)));
                    tax = gross - baseAmount.Value;
                }
                lines.Add(new PreparedLine(product, request.Quantity, request.UnitCost, mode, rate,
                    gross, baseAmount, tax, total));
            }
            var grossHeader = PurchaseCostPolicy.CheckedTwo(PurchaseCostPolicy.Round2(lines.Sum(x => x.Gross)));
            var vatHeader = mode == "NO_DISCRIMINADO" ? null :
                (decimal?)PurchaseCostPolicy.CheckedTwo(PurchaseCostPolicy.Round2(lines.Sum(x => x.Vat!.Value)));
            var totalHeader = mode == "SEPARADO" ?
                PurchaseCostPolicy.CheckedTwo(checked(grossHeader + vatHeader!.Value)) : grossHeader;
            return new PreparedPurchase(lines, grossHeader, vatHeader, totalHeader, null);
        }
        catch (OverflowException)
        {
            return PreparedPurchase.Fail("El costo, IVA o importe excede la precisión oficial.");
        }
    }

    private static DetalleCompra ToDetail(Guid purchaseId, PreparedLine line) => new()
    {
        Id = Guid.CreateVersion7(), CompraId = purchaseId, ProductoId = line.Product.Id,
        Cantidad = line.Quantity, ModoIva = line.Mode, CostoUnitarioDocumento = line.UnitCost,
        PorcentajeIva = line.Rate, SubtotalBruto = line.Gross, BaseGravable = line.TaxBase,
        ValorIva = line.Vat, TotalLinea = line.Total,
        CostoPromedioAnterior = line.Product.CostoPromedio,
        CostoPromedioResultante = line.Product.CostoPromedio ?? 0m,
        PrecioVentaAnterior = line.Product.PrecioMinorista, PrecioVentaNuevo = line.Product.PrecioMinorista,
        EsSustituto = false
    };

    private static PurchaseDto Map(Compra purchase) => new(purchase.Id, purchase.ProveedorId,
        purchase.NumeroDocumentoProveedor, purchase.FechaDocumento, purchase.ModoIva, purchase.Estado,
        purchase.Prefijo, purchase.Serie, purchase.Consecutivo, VisibleNumber(purchase),
        purchase.SubtotalBruto, purchase.ImpuestoTotal, purchase.Total, purchase.Version,
        purchase.DetalleCompra.OrderBy(x => x.ProductoId).Select(x => new PurchaseLineDto(x.Id, x.ProductoId,
            x.Cantidad, x.CostoUnitarioDocumento, x.ModoIva, x.PorcentajeIva, x.SubtotalBruto,
            x.BaseGravable, x.ValorIva, x.TotalLinea, x.CostoPromedioAnterior, x.CostoPromedioResultante)).ToArray());

    private static PurchaseSummaryDto MapSummary(Compra purchase) => new(purchase.Id, purchase.ProveedorId,
        purchase.NumeroDocumentoProveedor, purchase.FechaDocumento, purchase.Estado, VisibleNumber(purchase),
        purchase.Total, purchase.Version);

    private static string? VisibleNumber(Compra purchase) => purchase.Consecutivo is long number
        ? $"{purchase.Prefijo}-{purchase.Serie}-{number}" : null;

    private static string? NormalizeDocument(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().ToUpperInvariant();

    private static Result<PurchaseDto> DuplicateDocument() => Result.Failure<PurchaseDto>(
        ApplicationError.Conflict("El documento ya existe para este proveedor."));
    private static Result<PurchaseDto> Stale() => Result.Failure<PurchaseDto>(
        ApplicationError.ConcurrencyConflict("La compra fue modificada. Recargue los datos."));
    private static Result<T> Denied<T>() => Result.Failure<T>(ApplicationError.Forbidden());
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(ApplicationError.Validation(message));

    private sealed record ValidatedInput(string? Document, string? VatMode,
        IReadOnlyCollection<PurchaseLineRequest>? Lines, ApplicationError? Error)
    {
        public static ValidatedInput Fail(string message) => new(null, null, null, ApplicationError.Validation(message));
    }

    private sealed record PreparedLine(Producto Product, int Quantity, decimal UnitCost, string Mode,
        decimal? Rate, decimal Gross, decimal? TaxBase, decimal? Vat, decimal Total);

    private sealed record PreparedPurchase(IReadOnlyCollection<PreparedLine>? Lines, decimal Gross,
        decimal? Vat, decimal Total, ApplicationError? Error)
    {
        public static PreparedPurchase Fail(string message) => new(null, 0m, null, 0m, ApplicationError.Validation(message));
    }
}
