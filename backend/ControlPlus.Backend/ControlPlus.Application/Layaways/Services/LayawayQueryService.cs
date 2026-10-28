using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Services;

public sealed class LayawayQueryService(ILayawayQueryRepository layaways, IUserRepository users, IClock clock)
    : ILayawayQueryService
{
    public async Task<Result<PagedResult<LayawayQueryDto>>> ListAsync(ActorContext actor,
        LayawayListQuery query, CancellationToken cancellationToken = default)
    {
        var permission = await RequireManageAsync(actor, cancellationToken);
        if (permission is not null) return Result.Failure<PagedResult<LayawayQueryDto>>(permission);
        var offset = Validate(query);
        if (offset.Error is not null)
            return Result.Failure<PagedResult<LayawayQueryDto>>(offset.Error);

        var installation = await layaways.GetActiveInstallationAsync(cancellationToken);
        if (installation is null) return Result.Failure<PagedResult<LayawayQueryDto>>(ApplicationError.NotFound("la instalación activa"));
        var date = OperatingDate(installation);
        var result = await layaways.ListAsync(query with { State = query.State?.Trim().ToUpperInvariant() },
            offset.Value, installation.Id, date, cancellationToken);
        return Result.Success(new PagedResult<LayawayQueryDto>(result.Items.Select(x => Map(x, date)).ToArray(),
            result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<LayawayDetailDto>> GetAsync(ActorContext actor, Guid layawayId,
        CancellationToken cancellationToken = default)
    {
        var permission = await RequireManageAsync(actor, cancellationToken);
        if (permission is not null) return Result.Failure<LayawayDetailDto>(permission);
        var installation = await layaways.GetActiveInstallationAsync(cancellationToken);
        if (installation is null) return Result.Failure<LayawayDetailDto>(ApplicationError.NotFound("la instalación activa"));
        var operatingDate = OperatingDate(installation);
        var layaway = await layaways.GetAsync(layawayId, installation.Id, cancellationToken);
        if (layaway is null) return Result.Failure<LayawayDetailDto>(ApplicationError.NotFound("el apartado"));

        var lines = layaway.DetalleApartado.OrderBy(x => x.NombreProducto, StringComparer.Ordinal)
            .ThenBy(x => x.ProductoId)
            .Select(x => new LayawayLineDto(x.ProductoId, x.CodigoProducto, x.NombreProducto,
                x.UnidadMedida, x.Cantidad, x.PrecioReserva, x.SubtotalNeto)).ToArray();
        var payments = layaway.PagoApartado.OrderBy(x => x.FechaHora)
            .ThenBy(x => x.Consecutivo).ThenBy(x => x.Id)
            .Select(x => new LayawayPaymentDto(x.Id, Number(x.Prefijo, x.Serie, x.Consecutivo),
                x.MetodoPago.Codigo, x.MetodoPago.Nombre, x.ValorAplicado, x.ValorRecibido,
                x.Cambio, AsUtc(x.FechaHora), x.Estado, x.MovimientoCaja?.CorrelacionId)).ToArray();
        return Result.Success(new LayawayDetailDto(Map(layaway, operatingDate), layaway.PorcentajeMinimoInicial,
            lines, payments, MapDelivery(layaway), MapCancellation(layaway)));
    }

    private static LayawayCancellationDto? MapCancellation(Apartado layaway)
    {
        if (layaway.FechaAnulacion is not DateTime cancelledAt ||
            layaway.UsuarioAnulacionId is not Guid cancelledBy ||
            layaway.AutorizacionAnulacion?.UsuarioAutorizadorId is not Guid authorizerId ||
            layaway.TotalAbonadoCancelacion is not decimal paid ||
            layaway.ValorDevueltoCancelacion is not decimal refunded ||
            layaway.ValorRetenidoCancelacion is not decimal retained)
            return null;
        var rows = layaway.ReembolsoApartado.OrderBy(x => x.FechaHora).ThenBy(x => x.Id).ToArray();
        var correlation = layaway.AutorizacionAnulacion.CorrelacionId;
        return new LayawayCancellationDto(layaway.Id,
            Number(layaway.Prefijo, layaway.Serie, layaway.Consecutivo), layaway.Estado,
            layaway.Version, paid, refunded, retained, AsUtc(cancelledAt), cancelledBy,
            authorizerId, layaway.MotivoAnulacionId,
            layaway.ObservacionAnulacion, correlation,
            rows.Select(x => new LayawayRefundDto(x.Id, x.MetodoPago.Codigo,
                x.Valor, x.Referencia, AsUtc(x.FechaHora))).ToArray());
    }

    private static LayawayDeliveryDto? MapDelivery(Apartado layaway)
    {
        if (layaway.FechaEntrega is not DateTime deliveredAt ||
            layaway.UsuarioEntregaId is not Guid deliveredBy)
            return null;

        var movements = layaway.MovimientoInventario
            .Where(x => x.TipoMovimiento == "ENTREGA_APARTADO")
            .ToDictionary(x => x.ProductoId);
        if (movements.Count == 0) return null;

        var products = layaway.DetalleApartado
            .Where(x => movements.ContainsKey(x.ProductoId))
            .GroupBy(x => x.ProductoId).OrderBy(x => x.Key)
            .Select(x => x.OrderBy(line => line.Id).First())
            .Select(x => new LayawayDeliveredProductDto(x.ProductoId, x.CodigoProducto,
                x.NombreProducto, x.UnidadMedida, -movements[x.ProductoId].CantidadStock))
            .ToArray();
        return new LayawayDeliveryDto(layaway.Id,
            Number(layaway.Prefijo, layaway.Serie, layaway.Consecutivo), layaway.Estado,
            layaway.Version, AsUtc(deliveredAt), deliveredBy,
            movements.Values.First().CorrelacionId, products);
    }

    private async Task<ApplicationError?> RequireManageAsync(ActorContext actor,
        CancellationToken cancellationToken)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        return snapshot is null ? ApplicationError.Unauthorized()
            : snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase)
                ? null : ApplicationError.Forbidden();
    }

    private static (int Value, ApplicationError? Error) Validate(LayawayListQuery query)
    {
        var state = query.State?.Trim().ToUpperInvariant();
        if (query.Page <= 0 || query.PageSize is < 1 or > 100 ||
            (state is not null and not "ACTIVO" and not "PAGADO" and not "ENTREGADO" and not "ANULADO") ||
            (query.DueFrom is DateOnly from && query.DueTo is DateOnly to && from > to))
            return (0, ApplicationError.Validation("Filtros o paginación inválidos."));
        var offset = ((long)query.Page - 1L) * query.PageSize;
        return offset > int.MaxValue
            ? (0, ApplicationError.Validation("La página solicitada excede el rango admitido."))
            : ((int)offset, null);
    }

    private DateOnly OperatingDate(LayawayQueryInstallation installation) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(installation.TimeZoneId)).DateTime);

    private static LayawayQueryDto Map(Apartado x, DateOnly operatingDate) => new(x.Id,
        Number(x.Prefijo, x.Serie, x.Consecutivo), x.ClienteId, x.Cliente.NombreCompleto,
        AsUtc(x.FechaHora), x.FechaLimite, x.Total, x.AbonoInicial,
        x.SaldoPendiente, x.Estado, x.Version,
        x.Estado == "ACTIVO" && x.FechaLimite < operatingDate);

    private static string Number(string prefix, string series, long consecutive) =>
        $"{prefix}-{series}-{consecutive}";

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
