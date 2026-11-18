using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Establishments.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/establishment")]
public sealed class EstablishmentController(
    CurrentActorContextResolver actorContextResolver,
    IEstablishmentService establishment) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Datos públicos del establecimiento activo para la interfaz y los documentos.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<EstablishmentDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await establishment.GetAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Actualiza los datos del establecimiento; solo Administrador.</summary>
    [HttpPut]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [Consumes("application/json")]
    [ProducesResponseType<EstablishmentDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateEstablishmentRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await establishment.UpdateAsync(actor, request, cancellationToken), Ok);
    }
}
