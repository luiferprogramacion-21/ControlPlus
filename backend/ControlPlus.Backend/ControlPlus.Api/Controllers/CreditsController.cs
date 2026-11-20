using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Printing;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/credits")]
[OperatorScoped]
public sealed class CreditsController(CurrentActorContextResolver actorContextResolver,
    ICreditsService credits, ICashDrawerService drawer,
    ILogger<CreditsController> logger) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [ProducesResponseType<PagedResult<CreditDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List([FromQuery] CreditListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await credits.ListAsync(actor, query, cancellationToken), Ok);
    }

    [HttpGet("{creditId:guid}")]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [ProducesResponseType<CreditDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid creditId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await credits.GetAsync(actor, creditId, cancellationToken), Ok);
    }

    [HttpGet("{creditId:guid}/payments")]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [ProducesResponseType<PagedResult<CreditPaymentDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Payments(Guid creditId, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await credits.ListPaymentsAsync(actor, creditId, page, pageSize, cancellationToken), Ok);
    }

    [HttpPost("{creditId:guid}/payments")]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CreditPaymentOperationDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> ApplyPayment(Guid creditId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] ApplyCreditPaymentRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await credits.ApplyPaymentAsync(actor, creditId, idempotencyKey, request, cancellationToken);
        if (result.IsSuccess)
        {
            var cashPayment = result.Value!.Payments.FirstOrDefault(x =>
                x.MethodCode == "EFECTIVO" && x.AppliedAmount > 0);
            if (cashPayment is not null)
                await CashDrawerControllerDispatch.TryOpenAsync(drawer, actor,
                    CashDrawerOperationKind.CreditPayment, cashPayment.Id,
                    Response, logger, cancellationToken);
        }
        return FromResult(result, value => StatusCode(StatusCodes.Status201Created, value));
    }

    [HttpGet("payment-cancellation-reasons")]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [ProducesResponseType<IReadOnlyCollection<CreditPaymentCancellationReasonDto>>(
        StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> PaymentCancellationReasons(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(
            await credits.ListPaymentCancellationReasonsAsync(actor, cancellationToken), Ok);
    }

    [HttpPost("{creditId:guid}/payments/{paymentId:guid}/cancel")]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CancelCreditPaymentDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> CancelPayment(Guid creditId, Guid paymentId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] CancelCreditPaymentRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await credits.CancelPaymentAsync(
            actor, creditId, paymentId, idempotencyKey, request, cancellationToken), Ok);
    }

    [HttpPost("{creditId:guid}/forgive")]
    [PermissionAuthorize(PermissionCodes.CreditManage)]
    [Consumes("application/json")]
    [ProducesResponseType<ForgiveCreditDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Forgive(Guid creditId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] ForgiveCreditRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await credits.ForgiveAsync(
            actor, creditId, idempotencyKey, request, cancellationToken), Ok);
    }
}
