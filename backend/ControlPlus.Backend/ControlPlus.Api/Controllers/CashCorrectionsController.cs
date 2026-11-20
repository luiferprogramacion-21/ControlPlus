using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/cash")]
public sealed class CashCorrectionsController(CurrentActorContextResolver resolver, ICashService cash) : ApiControllerBase(resolver)
{
    [HttpPost("movements/{movementId:guid}/correction")]
    [PermissionAuthorize(PermissionCodes.CashMovementsManage)]
    public async Task<IActionResult> CorrectMovement(Guid movementId, [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] CorrectCashMovementRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await cash.CorrectMovementAsync(actor, movementId, key, request, ct), Ok);
    }
    [HttpGet("closed-shifts")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    public async Task<IActionResult> ClosedShifts(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await cash.ListClosedShiftsAsync(actor, ct), Ok);
    }
    [HttpGet("shifts/{shiftId:guid}/reviews")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    public async Task<IActionResult> Reviews(Guid shiftId, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await cash.ListClosureReviewsAsync(actor, shiftId, ct), Ok);
    }
    [HttpPost("shifts/{shiftId:guid}/reviews")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    public async Task<IActionResult> Review(Guid shiftId, [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] ReviewCashClosureRequest request, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await cash.ReviewClosureAsync(actor, shiftId, key, request, ct), Ok);
    }
}
