using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/credits")]
public sealed class CreditSettingsController(
    CurrentActorContextResolver actorContextResolver,
    ICreditSettingsService settings) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta el plazo predeterminado de los créditos del establecimiento activo.</summary>
    [HttpGet("settings")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<CreditSettingsDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.GetAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Actualiza el plazo sugerido para créditos futuros; no cambia créditos existentes.</summary>
    [HttpPut("settings")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CreditSettingsDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateCreditSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.UpdateAsync(actor, request, cancellationToken), Ok);
    }
}
