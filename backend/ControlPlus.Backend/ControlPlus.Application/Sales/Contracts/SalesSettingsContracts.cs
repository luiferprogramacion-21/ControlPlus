using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Sales.Contracts;

/// <summary>Configuración de precios del establecimiento activo que administra el Administrador.</summary>
public sealed record SalesSettingsDto(int WholesaleMinimumQuantity, long Version);

/// <summary>Regla mínima que necesita quien vende, aparta o cambia productos.</summary>
public sealed record SalesPricingRulesDto(int WholesaleMinimumQuantity);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSalesSettingsRequest(
    [property: JsonRequired] int WholesaleMinimumQuantity,
    [property: JsonRequired] long ExpectedVersion);

public interface ISalesSettingsService
{
    Task<Result<SalesSettingsDto>> GetAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SalesPricingRulesDto>> GetPricingRulesAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SalesSettingsDto>> UpdateAsync(ActorContext actor, UpdateSalesSettingsRequest request,
        CancellationToken cancellationToken = default);
}
