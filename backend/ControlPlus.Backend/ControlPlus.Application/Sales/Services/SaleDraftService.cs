using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Sales.Services;

/// <inheritdoc cref="ISaleDraftService"/>
public sealed class SaleDraftService(
    ISaleDraftRepository drafts,
    IPermissionChecker permissionChecker,
    IClock clock) : ISaleDraftService
{
    private const int MaximumLines = 300;
    private const int MaximumQuantity = 100_000;
    private static readonly string[] SaleTypes = ["CONTADO", "CREDITO"];
    private static readonly string[] PriceTypes = ["MINORISTA", "MAYORISTA"];
    private static readonly string[] DiscountTypes = ["PORCENTAJE", "VALOR_FIJO"];

    public async Task<Result<SaleDraftDto?>> GetCurrentAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireSalesCreateAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SaleDraftDto?>(error);
        var scope = await drafts.GetScopeAsync(cancellationToken);
        if (scope is null) return Result.Success<SaleDraftDto?>(null);
        var draft = await drafts.GetActiveAsync(actor.UserId, scope.ShiftId, track: false, cancellationToken);
        return Result.Success(draft is null || draft.DetalleBorradorVenta.Count == 0 ? null : Map(draft));
    }

    public async Task<Result<SaleDraftDto>> SaveAsync(ActorContext actor, SaveSaleDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var error = await RequireSalesCreateAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SaleDraftDto>(error);

        var saleType = request.SaleType?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SaleTypes.Contains(saleType)) return Invalid("El tipo de venta debe ser CONTADO o CREDITO.");
        var lines = request.Lines ?? [];
        if (lines.Count == 0) return Invalid("Un borrador necesita al menos un producto.");
        if (lines.Count > MaximumLines) return Invalid($"Un borrador admite hasta {MaximumLines} productos.");
        if (lines.Any(line => line is null || line.ProductId == Guid.Empty)) return Invalid("Cada línea necesita un producto.");
        if (lines.Select(line => line.ProductId).Distinct().Count() != lines.Count) return Invalid("Un producto aparece repetido.");
        if (lines.Any(line => line.Quantity is < 1 or > MaximumQuantity)) return Invalid("La cantidad debe ser un entero positivo.");
        var priceTypes = lines.Select(line => line.PriceType?.Trim().ToUpperInvariant() ?? string.Empty).ToArray();
        if (priceTypes.Any(type => !PriceTypes.Contains(type))) return Invalid("El tipo de precio debe ser MINORISTA o MAYORISTA.");
        if (lines.Any(line => line.Discount is not null && !ValidDiscount(line.Discount)) ||
            request.GeneralDiscount is not null && !ValidDiscount(request.GeneralDiscount))
            return Invalid("Un descuento no es válido.");
        var lineDiscounts = lines.Any(line => line.Discount is not null);
        if (lineDiscounts && request.GeneralDiscount is not null)
            return Invalid("El descuento va por producto o a toda la venta, no ambos.");
        var reason = request.DiscountReason?.Trim();
        if (reason is { Length: > 500 }) return Invalid("El motivo del descuento admite hasta 500 caracteres.");

        var scope = await drafts.GetScopeAsync(cancellationToken);
        if (scope is null) return Result.Failure<SaleDraftDto>(ApplicationError.Conflict("No hay un turno de Caja abierto."));
        var products = (await drafts.GetProductsAsync(lines.Select(line => line.ProductId).ToArray(), cancellationToken))
            .ToDictionary(product => product.Id);
        if (products.Count != lines.Count) return Invalid("Un producto del carrito ya no existe.");
        if (request.CustomerId is Guid customerId && !await drafts.CustomerExistsAsync(customerId, cancellationToken))
            return Invalid("El cliente ya no existe.");

        var now = clock.UtcNow.UtcDateTime;
        var draft = await drafts.GetActiveAsync(actor.UserId, scope.ShiftId, track: true, cancellationToken);
        var isNew = draft is null;
        draft ??= new BorradorVenta
        {
            Id = Guid.CreateVersion7(), InstalacionId = scope.InstallationId, TerminalId = scope.TerminalId,
            TurnoCajaId = scope.ShiftId, UsuarioId = actor.UserId, Estado = "ACTIVO", FechaCreacion = now,
            CorrelacionId = Guid.CreateVersion7(), Version = 1
        };
        draft.TipoVenta = saleType;
        draft.ClienteId = request.CustomerId;
        if (request.GeneralDiscount is { } general)
        {
            draft.AmbitoDescuento = "VENTA";
            draft.TipoDescuento = general.Type.Trim().ToUpperInvariant();
            draft.ValorDescuentoSolicitado = general.Value;
            draft.MotivoDescuento = string.IsNullOrEmpty(reason) ? null : reason;
        }
        else
        {
            // Por producto, el motivo es de la venta; sin descuentos, la tabla exige que no haya motivo.
            draft.AmbitoDescuento = lineDiscounts ? "PRODUCTO" : "NINGUNO";
            draft.TipoDescuento = null;
            draft.ValorDescuentoSolicitado = null;
            draft.MotivoDescuento = lineDiscounts && !string.IsNullOrEmpty(reason) ? reason : null;
        }
        if (!isNew) draft.FechaModificacion = now;

        var details = lines.Select((line, index) =>
        {
            var product = products[line.ProductId];
            var priceType = priceTypes[index];
            var discountType = line.Discount?.Type.Trim().ToUpperInvariant();
            return new DetalleBorradorVenta
            {
                Id = Guid.CreateVersion7(), BorradorVentaId = draft.Id, ProductoId = product.Id, Cantidad = line.Quantity,
                TipoPrecio = priceType,
                PrecioUnitario = decimal.Round(priceType == "MAYORISTA" && product.PrecioMayorista is decimal wholesale
                    ? wholesale : product.PrecioMinorista, 0, MidpointRounding.AwayFromZero),
                TipoDescuento = discountType,
                PorcentajeDescuento = discountType == "PORCENTAJE" ? line.Discount!.Value : null,
                ValorDescuento = discountType == "VALOR_FIJO" ? line.Discount!.Value : 0,
                Orden = index, FechaModificacion = now, Version = 1
            };
        }).ToArray();

        await drafts.SaveAsync(draft, isNew, details, now, cancellationToken);
        draft.DetalleBorradorVenta = details;
        return Result.Success(Map(draft));
    }

    public async Task<Result> DiscardAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireSalesCreateAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure(error);
        await drafts.DiscardActiveAsync(actor.UserId, clock.UtcNow.UtcDateTime, cancellationToken);
        return Result.Success();
    }

    private async Task<ApplicationError?> RequireSalesCreateAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId == Guid.Empty) return ApplicationError.Unauthorized();
        return await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.SalesCreate, cancellationToken)
            ? null : ApplicationError.Forbidden();
    }

    private static bool ValidDiscount(SaleDiscountRequest discount)
    {
        var type = discount.Type?.Trim().ToUpperInvariant();
        if (!DiscountTypes.Contains(type)) return false;
        return type == "PORCENTAJE"
            ? discount.Value is > 0 and <= 100 && decimal.Round(discount.Value, 2) == discount.Value
            : discount.Value > 0 && decimal.Truncate(discount.Value) == discount.Value && discount.Value <= 999_999_999_999m;
    }

    private static Result<SaleDraftDto> Invalid(string message) => Result.Failure<SaleDraftDto>(ApplicationError.Validation(message));

    private static SaleDraftDto Map(BorradorVenta draft) => new(
        draft.Id, draft.TipoVenta, draft.ClienteId,
        draft.DetalleBorradorVenta.OrderBy(line => line.Orden).Select(line => new SaleDraftLineDto(
            line.ProductoId, line.Cantidad, line.TipoPrecio,
            line.TipoDescuento switch
            {
                "PORCENTAJE" => new SaleDiscountRequest("PORCENTAJE", line.PorcentajeDescuento ?? 0),
                "VALOR_FIJO" => new SaleDiscountRequest("VALOR_FIJO", line.ValorDescuento),
                _ => null
            })).ToArray(),
        draft.AmbitoDescuento == "VENTA" && draft.TipoDescuento is not null && draft.ValorDescuentoSolicitado is decimal value
            ? new SaleDiscountRequest(draft.TipoDescuento, value) : null,
        draft.MotivoDescuento,
        new DateTimeOffset(DateTime.SpecifyKind(draft.FechaModificacion ?? draft.FechaCreacion, DateTimeKind.Utc)));
}
