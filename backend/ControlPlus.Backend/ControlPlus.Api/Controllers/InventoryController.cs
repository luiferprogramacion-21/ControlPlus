using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/inventory")]
public sealed class InventoryController(
    CurrentActorContextResolver actorContextResolver,
    IInventoryService inventoryService) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta existencias actuales y el ultimo movimiento de cada producto.</summary>
    [HttpGet("stock")]
    [PermissionAuthorize(PermissionCodes.InventoryRead)]
    [ProducesResponseType<PagedResult<InventoryStockDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ListStock(
        [FromQuery] InventoryStockQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await inventoryService.ListStockAsync(actor, query, cancellationToken), Ok);
    }

    /// <summary>Consulta el kardex inmutable de inventario.</summary>
    [HttpGet("movements")]
    [PermissionAuthorize(PermissionCodes.InventoryRead)]
    [ProducesResponseType<PagedResult<InventoryMovementDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ListMovements(
        [FromQuery] InventoryMovementQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await inventoryService.ListMovementsAsync(actor, query, cancellationToken), Ok);
    }

    /// <summary>Lista motivos activos oficiales para ajustes manuales.</summary>
    [HttpGet("adjustment-reasons")]
    [PermissionAuthorize(PermissionCodes.InventoryAdjust)]
    [ProducesResponseType<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ListAdjustmentReasons(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await inventoryService.ListAdjustmentReasonsAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Registra un ajuste idempotente con doble control y actualiza el stock en la misma transaccion.</summary>
    [HttpPost("adjustments")]
    [PermissionAuthorize(PermissionCodes.InventoryAdjust)]
    [Consumes("application/json")]
    [ProducesResponseType<InventoryMovementDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Adjust(
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] CreateInventoryAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await inventoryService.AdjustAsync(actor, idempotencyKey, request, cancellationToken);
        return FromResult(result, value => Created($"/api/inventory/movements?productId={value.ProductId}", value));
    }
}
