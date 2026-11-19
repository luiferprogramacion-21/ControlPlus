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

/// <summary>Venta en espera: lo justo para reconocerla en la lista. El total es aproximado (precios del momento en que se guardó).</summary>
public sealed record HeldSaleDto(
    Guid Id,
    string SaleType,
    Guid? CustomerId,
    string? CustomerName,
    int Products,
    int Units,
    decimal ApproximateTotal,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Venta recuperable (decisión del 18 de noviembre de 2026) y ventas en espera (decisión del 19 de noviembre de 2026):
/// la pantalla guarda cada carrito en <c>ventas.borrador_venta</c>. El que está en pantalla se recupera si se cierra la
/// sesión, se va la luz o se cierra el navegador; los demás quedan en espera para retomarlos. Cada usuario tiene hasta
/// diez abiertos en el turno abierto; al confirmar una venta se cierra como CONVERTIDO solo su borrador, en la misma
/// transacción. No es una acción de negocio: no se audita ni reserva inventario.
/// </summary>
public interface ISaleDraftService
{
    /// <summary>El borrador abierto más reciente (contrato de la venta recuperable).</summary>
    Task<Result<SaleDraftDto?>> GetCurrentAsync(ActorContext actor, CancellationToken cancellationToken = default);
    /// <summary>Reemplaza el borrador abierto más reciente o crea uno (contrato de la venta recuperable).</summary>
    Task<Result<SaleDraftDto>> SaveAsync(ActorContext actor, SaveSaleDraftRequest request, CancellationToken cancellationToken = default);
    /// <summary>Descarta todos los borradores abiertos del usuario.</summary>
    Task<Result> DiscardAsync(ActorContext actor, CancellationToken cancellationToken = default);

    /// <summary>Ventas sin terminar del usuario en el turno abierto, de la más reciente a la más antigua.</summary>
    Task<Result<IReadOnlyCollection<HeldSaleDto>>> ListAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SaleDraftDto>> GetAsync(ActorContext actor, Guid draftId, CancellationToken cancellationToken = default);
    /// <summary>Guarda un carrito nuevo como otro borrador. 409 si ya hay diez abiertos.</summary>
    Task<Result<SaleDraftDto>> CreateAsync(ActorContext actor, SaveSaleDraftRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reemplaza un borrador abierto del usuario. 404 si ya se cerró o es de otro.</summary>
    Task<Result<SaleDraftDto>> SaveAsync(ActorContext actor, Guid draftId, SaveSaleDraftRequest request, CancellationToken cancellationToken = default);
    Task<Result> DiscardAsync(ActorContext actor, Guid draftId, CancellationToken cancellationToken = default);
}
