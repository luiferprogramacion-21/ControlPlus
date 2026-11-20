using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Sales;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    public async Task<Result<SaleChangePreparationDto>> FindChangeSaleAsync(ActorContext actor,
        string visibleNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(visibleNumber) || visibleNumber.Length > 80)
            return Result.Failure<SaleChangePreparationDto>(Validation("El número visible de venta es inválido."));
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<SaleChangePreparationDto>(ApplicationError.Unauthorized());
        var sale = await repository.FindSaleByVisibleNumberAsync(visibleNumber.Trim(), cancellationToken);
        if (sale is null) return Result.Failure<SaleChangePreparationDto>(ApplicationError.NotFound("la venta"));
        return await PrepareChangeAsync(actor, authorization.PermissionCodes, sale, cancellationToken);
    }

    public async Task<Result<SaleChangePreparationDto>> GetChangePreparationAsync(ActorContext actor,
        Guid saleId, CancellationToken cancellationToken = default)
    {
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<SaleChangePreparationDto>(ApplicationError.Unauthorized());
        var sale = await repository.GetSaleAsync(saleId, cancellationToken);
        if (sale is null) return Result.Failure<SaleChangePreparationDto>(ApplicationError.NotFound("la venta"));
        return await PrepareChangeAsync(actor, authorization.PermissionCodes, sale, cancellationToken);
    }

    private async Task<Result<SaleChangePreparationDto>> PrepareChangeAsync(ActorContext actor,
        IReadOnlyCollection<string> permissionCodes, Venta sale, CancellationToken cancellationToken)
    {
        var canReadAll = permissionCodes.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase);
        var canReadOwn = permissionCodes.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);
        if (!canReadAll && !(canReadOwn && sale.UsuarioId == actor.UserId))
            return Result.Failure<SaleChangePreparationDto>(ApplicationError.Forbidden());
        var establishment = await repository.GetActiveEstablishmentAsync(cancellationToken);
        if (establishment is null)
            return Result.Failure<SaleChangePreparationDto>(ApplicationError.NotFound("el establecimiento activo"));
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(establishment.ZonaHoraria)).DateTime);
        var returned = await repository.GetConfirmedReturnedQuantitiesAsync(sale.Id, cancellationToken);
        var lines = sale.DetalleVenta.OrderBy(x => x.ProductoId).Select(line =>
        {
            var returnedQuantity = returned.GetValueOrDefault(line.Id);
            var remaining = SaleChangeAvailability.RemainingQuantity(line.Cantidad, returnedQuantity);
            return new SaleChangeLineAvailabilityDto(line.Id, line.ProductoId, line.CodigoProducto,
                line.NombreProducto, line.Cantidad, returnedQuantity, remaining);
        }).ToArray();
        var withinDeadline = SaleChangeAvailability.IsWithinDeadline(localDate, sale.FechaLimiteCambio);
        var eligibleType = sale.TipoVenta == "CONTADO" ||
            sale.TipoVenta == "CREDITO" && sale.Credito is { Estado: "PENDIENTE" or "VENCIDO" };
        var blockedReason = sale.Estado != "CONFIRMADA" ? "La venta está anulada; consulta su historial."
            : !withinDeadline ? $"El plazo para corregir productos venció el {sale.FechaLimiteCambio:dd/MM/yyyy}."
            : !eligibleType ? "El crédito ya está pagado o anulado. Esta venta requiere revisión del Administrador; no se modifica su saldo ni se simula un abono."
            : !lines.Any(x => x.RemainingQuantity > 0) ? "Todos los productos originales ya se devolvieron. Los productos recibidos en un cambio anterior requieren revisión del Administrador."
            : null;
        return Result.Success(new SaleChangePreparationDto(sale.Id, VisibleNumber(sale), sale.TipoVenta,
            sale.Estado, sale.Version, sale.FechaOperativa, sale.FechaLimiteCambio,
            localDate, withinDeadline,
            sale.Estado == "CONFIRMADA" && eligibleType && withinDeadline &&
            lines.Any(x => x.RemainingQuantity > 0), lines,
            sale.Credito?.Id, sale.Credito?.Version, sale.Credito?.SaldoPendiente, blockedReason));
    }
}
