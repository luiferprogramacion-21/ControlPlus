using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/layaways/{layawayId:guid}/delivery")]
[OperatorScoped]
public sealed class LayawayDeliveryController(CurrentActorContextResolver actorContextResolver,
    ILayawayDeliveryService delivery) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Entrega todos los productos de un apartado PAGADO sin cobrar ni crear una venta.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.LayawayManage)]
    [Consumes("application/json")]
    [ProducesResponseType<LayawayDeliveryDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Deliver(Guid layawayId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] DeliverLayawayRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(
            await delivery.DeliverAsync(actor, layawayId, idempotencyKey, request, cancellationToken), Ok);
    }
}
