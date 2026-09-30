using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/document-numbering/sales")]
public sealed class DocumentNumberingController(
    CurrentActorContextResolver actorContextResolver,
    IDocumentNumberingService numberingService) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta el consecutivo oficial de VENTAS para la instalación y serie activas.</summary>
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
        return actor is null
            ? MissingActor()
            : FromResult(await numberingService.GetSalesAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Crea el consecutivo oficial de VENTAS para la instalación y serie activas.</summary>
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
        [FromBody] CreateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await numberingService.CreateSalesAsync(actor, request, cancellationToken);
        return FromResult(result, numbering => CreatedAtAction(nameof(Get), numbering));
    }

    /// <summary>Corrige el consecutivo oficial de VENTAS antes de la primera venta confirmada.</summary>
    [HttpPut]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [Consumes("application/json")]
    [ProducesResponseType<SalesDocumentNumberingDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await numberingService.UpdateSalesAsync(actor, request, cancellationToken), Ok);
    }
}
