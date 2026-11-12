using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Credits.Contracts;

/// <summary>Plazo predeterminado de los créditos del establecimiento activo.</summary>
public sealed record CreditSettingsDto(int? DefaultDays, long Version);

/// <summary>Regla mínima que necesita quien prepara una venta a crédito.</summary>
public sealed record CreditRulesDto(int? DefaultDays);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCreditSettingsRequest(
    [property: JsonRequired] int? DefaultDays,
    [property: JsonRequired] long ExpectedVersion);

public interface ICreditSettingsService
{
    Task<Result<CreditSettingsDto>> GetAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CreditRulesDto>> GetSalesRulesAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CreditSettingsDto>> UpdateAsync(
        ActorContext actor, UpdateCreditSettingsRequest request,
        CancellationToken cancellationToken = default);
}
