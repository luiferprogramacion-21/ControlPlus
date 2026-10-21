using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/document-numbering/layaway-payments")]
public sealed class LayawayPaymentNumberingController(
    CurrentActorContextResolver actorContextResolver,
    IDocumentNumberingService numberingService) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta el consecutivo PAGO_APARTADO de la instalación y serie activas.</summary>
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [ProducesResponseType<SalesDocumentNumberingDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() :
            FromResult(await numberingService.GetLayawayPaymentsAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Inicializa PAGO_APARTADO con prefijo PAP y primer número 1.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [Consumes("application/json")]
    [ProducesResponseType<SalesDocumentNumberingDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(
        [FromBody] CreateLayawayDocumentNumberingRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await numberingService.CreateLayawayPaymentsAsync(actor, request, cancellationToken);
        return FromResult(result, numbering => CreatedAtAction(nameof(Get), numbering));
    }
}
