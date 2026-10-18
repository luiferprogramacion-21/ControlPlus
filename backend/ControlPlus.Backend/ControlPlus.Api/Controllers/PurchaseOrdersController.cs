using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.Purchases.Contracts;
using System.ComponentModel.DataAnnotations;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/purchase-orders")]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
public sealed class PurchaseOrdersController(CurrentActorContextResolver actors, IPurchaseOrderService orders,
    IPurchaseService purchases)
    : ApiControllerBase(actors)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [ProducesResponseType<PagedResult<PurchaseOrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PurchaseOrderListQuery query, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.ListAsync(actor, query, ct), Ok);
    }

    [HttpGet("{orderId:guid}")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid orderId, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.GetAsync(actor, orderId, ct), Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.CreateAsync(actor, request, ct),
            value => CreatedAtAction(nameof(Get), new { orderId = value.Id }, value));
    }

    [HttpPut("{orderId:guid}")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid orderId, [FromBody] UpdatePurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.UpdateAsync(actor, orderId, request, ct), Ok);
    }

    [HttpPost("{orderId:guid}/send")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid orderId, [FromBody] SendPurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.SendAsync(actor, orderId, request, ct), Ok);
    }

    [HttpPost("{orderId:guid}/receipts")]
    [PermissionAuthorize(PermissionCodes.PurchasesReceive)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseReceiptDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Receive(Guid orderId,
        [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] ReceivePurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(
            await purchases.ReceiveOrderAsync(actor, orderId, key, request, ct),
            value => CreatedAtAction(nameof(PurchasesController.Get), "Purchases",
                new { purchaseId = value.Purchase.Id }, value));
    }

    [HttpPost("{orderId:guid}/close")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(Guid orderId, [FromBody] ClosePurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.CloseAsync(actor, orderId, request, ct), Ok);
    }

    [HttpPost("{orderId:guid}/cancel")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersCancel)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid orderId, [FromBody] CancelPurchaseOrderRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.CancelAsync(actor, orderId, request, ct), Ok);
    }

    [HttpGet("reasons/close")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [ProducesResponseType<IReadOnlyCollection<PurchaseOrderReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CloseReasons(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.ListReasonsAsync(actor, "close", ct), Ok);
    }

    [HttpPost("reasons/close/initialize")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersManage)]
    [ProducesResponseType<IReadOnlyCollection<PurchaseOrderReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitializeCloseReasons(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.InitializeReasonsAsync(actor, "close", ct), Ok);
    }

    [HttpGet("reasons/cancel")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersCancel)]
    [ProducesResponseType<IReadOnlyCollection<PurchaseOrderReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelReasons(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.ListReasonsAsync(actor, "cancel", ct), Ok);
    }

    [HttpPost("reasons/cancel/initialize")]
    [PermissionAuthorize(PermissionCodes.PurchaseOrdersCancel)]
    [ProducesResponseType<IReadOnlyCollection<PurchaseOrderReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitializeCancelReasons(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.InitializeReasonsAsync(actor, "cancel", ct), Ok);
    }

    [HttpGet("numbering")]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [ProducesResponseType<PurchaseOrderNumberingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Numbering(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.GetNumberingAsync(actor, ct), Ok);
    }

    [HttpPost("numbering/initialize")]
    [PermissionAuthorize(PermissionCodes.NumberingManage)]
    [ProducesResponseType<PurchaseOrderNumberingDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitializeNumbering(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await orders.InitializeNumberingAsync(actor, ct), Ok);
    }
}
