using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Sales.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleDraftLineRequest(Guid ProductId, int Quantity, string PriceType, SaleDiscountRequest? Discount = null);

/// <summary>Carrito en curso tal como está en la pantalla. Reemplaza por completo el borrador anterior del usuario.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveSaleDraftRequest(
    string SaleType,
    Guid? CustomerId,
    IReadOnlyCollection<SaleDraftLineRequest> Lines,
    SaleDiscountRequest? GeneralDiscount = null,
    string? DiscountReason = null);

public sealed record SaleDraftLineDto(Guid ProductId, int Quantity, string PriceType, SaleDiscountRequest? Discount);

public sealed record SaleDraftDto(
    Guid Id,
    string SaleType,
    Guid? CustomerId,
    IReadOnlyCollection<SaleDraftLineDto> Lines,
    SaleDiscountRequest? GeneralDiscount,
    string? DiscountReason,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Venta recuperable (decisión del 18 de noviembre de 2026): la pantalla guarda el carrito en curso en
/// <c>ventas.borrador_venta</c> para recuperarlo si se cierra la sesión, se va la luz o se cierra el navegador. Cada
/// usuario tiene a lo sumo un borrador activo en el turno abierto; al confirmar la venta se cierra como CONVERTIDO en la
/// misma transacción. No es una acción de negocio: no se audita ni reserva inventario.
/// </summary>
public interface ISaleDraftService
{
    Task<Result<SaleDraftDto?>> GetCurrentAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SaleDraftDto>> SaveAsync(ActorContext actor, SaveSaleDraftRequest request, CancellationToken cancellationToken = default);
    Task<Result> DiscardAsync(ActorContext actor, CancellationToken cancellationToken = default);
}
