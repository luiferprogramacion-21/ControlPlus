using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/layaways/{layawayId:guid}/payments")]
public sealed class LayawayPaymentsController(CurrentActorContextResolver actorContextResolver,
    ILayawayPaymentService payments) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Abona un apartado ACTIVO con uno a tres medios y numeración PAP.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.LayawayManage)]
    [Consumes("application/json")]
    [ProducesResponseType<LayawayPaymentOperationDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Apply(Guid layawayId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] ApplyLayawayPaymentRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(
            await payments.ApplyAsync(actor, layawayId, idempotencyKey, request, cancellationToken), Ok);
    }
}
