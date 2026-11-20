using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/cash/drawer")]
[Produces("application/json", "application/problem+json")]
public sealed class CashDrawerController(CurrentActorContextResolver actorResolver,
    ICashDrawerService drawer) : ApiControllerBase(actorResolver)
{
    [HttpGet("status")]
    [PermissionAuthorize(PermissionCodes.CashOwnRead)]
    [ProducesResponseType<CashDrawerStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await drawer.GetStatusAsync(actor, ct), Ok);
    }

    /// <summary>Al pulsar «Cobrar» en el punto de venta (decisión del 19 de noviembre de 2026).</summary>
    [HttpPost("checkout")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType<CashDrawerOpenResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenForCheckout(
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null) return MissingActor();
        if (!Guid.TryParse(idempotencyKey, out var checkoutId))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["La clave del cobro debe ser un UUID."]
            }));
        return FromResult(await drawer.OpenForCheckoutAsync(actor, checkoutId, ct), Ok);
    }

    /// <summary>Un Supervisor o Administrador solicita un pulso con motivo. Windows solo confirma el envío.</summary>
    [HttpPost("open")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashDrawerOpenResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Open(
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] OpenCashDrawerRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null) return MissingActor();
        if (!Guid.TryParse(idempotencyKey, out var requestId))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["La clave de apertura manual debe ser un UUID."]
            }));
        return FromResult(await drawer.OpenManualAsync(actor, requestId, request, ct), Ok);
    }
}
