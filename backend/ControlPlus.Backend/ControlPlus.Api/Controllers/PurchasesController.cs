using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/purchases")]
public sealed class PurchasesController(CurrentActorContextResolver actors, IPurchaseService purchases)
    : ApiControllerBase(actors)
{
    [HttpGet("{purchaseId:guid}/correction-preparation")]
    [PermissionAuthorize(PermissionCodes.PurchasesCancel)]
    [ProducesResponseType<PurchaseCorrectionPreparationDto>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> PrepareCorrection(Guid purchaseId, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.PrepareCorrectionAsync(actor, purchaseId, ct), Ok);
    }

    [HttpPost("{purchaseId:guid}/correct-receipt")]
    [PermissionAuthorize(PermissionCodes.PurchasesCancel)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDto>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> CorrectReceipt(Guid purchaseId,
        [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] CancelPurchaseRequest request, CancellationToken ct)
    {
        if (request.ExpectedOrderVersion is null) return BadRequest(new ValidationProblemDetails
            { Title = "La corrección de recepción requiere la versión vigente del pedido." });
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.CancelAsync(actor, purchaseId, key, request, ct), Ok);
    }

    [HttpGet("cancellation-reasons")]
    [PermissionAuthorize(PermissionCodes.PurchasesCancel)]
    [ProducesResponseType<IReadOnlyCollection<PurchaseCancellationReasonDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> ListCancellationReasons(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.ListCancellationReasonsAsync(actor, ct), Ok);
    }

    [HttpPost("cancellation-reasons/initialize")]
    [PermissionAuthorize(PermissionCodes.PurchasesCancel)]
    [ProducesResponseType<IReadOnlyCollection<PurchaseCancellationReasonDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> InitializeCancellationReasons(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.InitializeCancellationReasonsAsync(actor, ct), Ok);
    }

    [HttpPost("{purchaseId:guid}/cancel")]
    [PermissionAuthorize(PermissionCodes.PurchasesCancel)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Cancel(Guid purchaseId,
        [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] CancelPurchaseRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.CancelAsync(actor, purchaseId, key, request, ct), Ok);
    }
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.PurchasesReceive)]
    [ProducesResponseType<PagedResult<PurchaseSummaryDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> List([FromQuery] PurchaseListQuery query, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.ListAsync(actor, query, ct), Ok);
    }

    [HttpGet("{purchaseId:guid}")]
    [PermissionAuthorize(PermissionCodes.PurchasesReceive)]
    [ProducesResponseType<PurchaseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Get(Guid purchaseId, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.GetAsync(actor, purchaseId, ct), Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.PurchasesReceive)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.CreateAsync(actor, request, ct),
            value => CreatedAtAction(nameof(Get), new { purchaseId = value.Id }, value));
    }

    [HttpPut("{purchaseId:guid}")]
    [PermissionAuthorize(PermissionCodes.PurchasesReceive)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid purchaseId, [FromBody] UpdatePurchaseRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.UpdateAsync(actor, purchaseId, request, ct), Ok);
    }

    [HttpPost("{purchaseId:guid}/confirm")]
    [PermissionAuthorize(PermissionCodes.PurchasesReceive)]
    [Consumes("application/json")]
    [ProducesResponseType<PurchaseDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Confirm(Guid purchaseId,
        [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] ConfirmPurchaseRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await purchases.ConfirmAsync(actor, purchaseId, key, request, ct), Ok);
    }
}
