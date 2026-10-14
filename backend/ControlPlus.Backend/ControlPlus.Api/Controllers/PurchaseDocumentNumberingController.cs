using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/document-numbering/purchases")]
public sealed class PurchaseDocumentNumberingController(
    CurrentActorContextResolver actorContextResolver,
    IDocumentNumberingService numberingService) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta el consecutivo COMPRA de la instalación y serie activas.</summary>
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [ProducesResponseType<PurchaseDocumentNumberingDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await numberingService.GetPurchasesAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Crea el consecutivo COMPRA con prefijo CMP y primer número 1.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDocumentNumberingDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseDocumentNumberingRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await numberingService.CreatePurchasesAsync(actor, request, cancellationToken);
        return FromResult(result, numbering => CreatedAtAction(nameof(Get), numbering));
    }

    /// <summary>Corrige el primer número antes de la primera compra numerada.</summary>
    [HttpPut]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDocumentNumberingDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(
        [FromBody] UpdatePurchaseDocumentNumberingRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await numberingService.UpdatePurchasesAsync(actor, request, cancellationToken), Ok);
    }
}
