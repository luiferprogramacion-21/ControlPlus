using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Printing;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/layaways")]
public sealed class LayawayCreateController(
    CurrentActorContextResolver actorContextResolver,
    ILayawayCreateService layaways, ICashDrawerService drawer,
    ILogger<LayawayCreateController> logger) : ApiControllerBase(actorContextResolver)
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
        if (actor is null) return MissingActor();
        var result = await layaways.CreateAsync(actor, idempotencyKey, request, cancellationToken);
        if (result.IsSuccess && result.Value!.Payments.Any(x =>
                x.MethodCode == "EFECTIVO" && x.AppliedAmount > 0))
            await CashDrawerControllerDispatch.TryOpenAsync(drawer, actor,
                CashDrawerOperationKind.LayawayInitial, result.Value.Id,
                Response, logger, cancellationToken);
        return FromResult(result, created => Created($"/api/layaways/{created.Id}", created));
    }
}
