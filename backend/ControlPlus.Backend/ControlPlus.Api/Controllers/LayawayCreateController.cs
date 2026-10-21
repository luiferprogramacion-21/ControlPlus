using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/layaways")]
public sealed class LayawayCreateController(
    CurrentActorContextResolver actorContextResolver,
    ILayawayCreateService layaways) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Crea un apartado, reserva existencias y registra el abono inicial en una transacción.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.LayawayManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CreatedLayawayDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] CreateLayawayRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(
            await layaways.CreateAsync(actor, idempotencyKey, request, cancellationToken),
            created => Created($"/api/layaways/{created.Id}", created));
    }
}
