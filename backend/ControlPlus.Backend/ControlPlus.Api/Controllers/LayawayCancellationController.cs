using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Printing;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/layaways/{layawayId:guid}/cancellation")]
[OperatorScoped]
public sealed class LayawayCancellationController(CurrentActorContextResolver actorContextResolver,
    ILayawayCancellationService cancellation, ICashDrawerService drawer,
    ILogger<LayawayCancellationController> logger) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Cancela un apartado y devuelve hasta el total abonado, por medios de pago distintos.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.LayawayCancel)]
    [Consumes("application/json")]
    [ProducesResponseType<LayawayCancellationDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Cancel(Guid layawayId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] CancelLayawayRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await cancellation.CancelAsync(actor, layawayId, idempotencyKey, request, cancellationToken);
        if (result.IsSuccess)
        {
            var cashRefund = result.Value!.Refunds.FirstOrDefault(x =>
                x.MethodCode == "EFECTIVO" && x.Amount > 0);
            if (cashRefund is not null)
                await CashDrawerControllerDispatch.TryOpenAsync(drawer, actor,
                    CashDrawerOperationKind.LayawayRefund, cashRefund.Id,
                    Response, logger, cancellationToken);
        }
        return FromResult(result, Ok);
    }
}
