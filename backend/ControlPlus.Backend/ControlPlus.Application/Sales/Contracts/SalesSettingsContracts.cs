using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Sales.Contracts;

/// <summary>Configuración de precios del establecimiento activo que administra el Administrador.</summary>
/// Decisión D7 del 23/11/2026: incluye el descuento máximo del Cajero y del Supervisor, que configura el Administrador.
public sealed record SalesSettingsDto(int WholesaleMinimumQuantity, long Version,
    decimal CashierMaxDiscountPercent = 5m, decimal SupervisorMaxDiscountPercent = 20m);

/// <summary>Reglas que necesita quien vende, aparta o cambia productos, con el descuento máximo de cada rol.</summary>
public sealed record SalesPricingRulesDto(int WholesaleMinimumQuantity,
    decimal CashierMaxDiscountPercent = 5m, decimal SupervisorMaxDiscountPercent = 20m,
    decimal AdministratorMaxDiscountPercent = 100m);

/// <summary>Los dos límites son opcionales: si no se envían, se conservan.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSalesSettingsRequest(
    [property: JsonRequired] int WholesaleMinimumQuantity,
    [property: JsonRequired] long ExpectedVersion,
    decimal? CashierMaxDiscountPercent = null,
    decimal? SupervisorMaxDiscountPercent = null);

public interface ISalesSettingsService
{
    Task<Result<SalesSettingsDto>> GetAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SalesPricingRulesDto>> GetPricingRulesAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SalesSettingsDto>> UpdateAsync(ActorContext actor, UpdateSalesSettingsRequest request,
        CancellationToken cancellationToken = default);
}
