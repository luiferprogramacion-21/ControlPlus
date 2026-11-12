using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Layaways.Contracts;

/// <summary>Parámetros de apartados del establecimiento activo. <c>DefaultDays</c> nulo obliga a elegir la fecha límite en cada apartado.</summary>
public sealed record LayawaySettingsDto(
    decimal MinimumPercentage,
    int? DefaultDays,
    bool RequireMinimumInitial,
    long Version);

/// <summary>Reglas necesarias para crear un apartado, sin exponer la versión de configuración.</summary>
public sealed record LayawayCreationRulesDto(
    bool RequireMinimumInitial,
    decimal MinimumPercentage,
    int? DefaultDays);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLayawaySettingsRequest(
    [property: JsonRequired] decimal MinimumPercentage,
    int? DefaultDays,
    [property: JsonRequired] bool RequireMinimumInitial,
    [property: JsonRequired] long ExpectedVersion);

public interface ILayawaySettingsService
{
    Task<Result<LayawaySettingsDto>> GetAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<LayawayCreationRulesDto>> GetCreationRulesAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<LayawaySettingsDto>> UpdateAsync(ActorContext actor, UpdateLayawaySettingsRequest request,
        CancellationToken cancellationToken = default);
}
