using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/sales")]
[Produces("application/json", "application/problem+json")]
public sealed class SalesController(
    CurrentActorContextResolver actorContextResolver,
    ISalesService salesService) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Confirma una venta CONTADO idempotente y descuenta su inventario.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [Consumes("application/json")]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] ConfirmSaleRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await salesService.ConfirmAsync(actor, idempotencyKey, request, cancellationToken);
        return FromResult(result, sale => CreatedAtAction(nameof(Get), new { saleId = sale.Id }, sale));
    }

    /// <summary>Consulta las ventas del usuario autenticado.</summary>
    [HttpGet("own")]
    [PermissionAuthorize(PermissionCodes.SalesOwnRead)]
    [ProducesResponseType<PagedResult<SaleSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOwn([FromQuery] SaleListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.ListOwnAsync(actor, query, cancellationToken), Ok);
    }

    /// <summary>Consulta el historial general de ventas.</summary>
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.SalesAllRead)]
    [ProducesResponseType<PagedResult<SaleSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAll([FromQuery] SaleListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.ListAllAsync(actor, query, cancellationToken), Ok);
    }

    /// <summary>Consulta una venta si pertenece al actor o si este puede leer el historial general.</summary>
    [HttpGet("{saleId:guid}")]
    [Authorize]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid saleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.GetAsync(actor, saleId, cancellationToken), Ok);
    }
}
