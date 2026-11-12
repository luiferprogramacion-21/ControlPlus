using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/layaways")]
public sealed class LayawaySettingsController(
    CurrentActorContextResolver actorContextResolver,
    ILayawaySettingsService settings) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta el porcentaje mínimo del pago inicial y el plazo por defecto de los apartados.</summary>
    [HttpGet("settings")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<LayawaySettingsDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await settings.GetAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Actualiza los parámetros de apartados. Solo afecta a apartados futuros.</summary>
    [HttpPut("settings")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [Consumes("application/json")]
    [ProducesResponseType<LayawaySettingsDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update([FromBody] UpdateLayawaySettingsRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await settings.UpdateAsync(actor, request, cancellationToken), Ok);
    }

    /// <summary>Consulta las reglas mínimas necesarias para crear un apartado.</summary>
    [HttpGet("creation-rules")]
    [PermissionAuthorize(PermissionCodes.LayawayManage)]
    [ProducesResponseType<LayawayCreationRulesDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetCreationRules(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.GetCreationRulesAsync(actor, cancellationToken), Ok);
    }
}
