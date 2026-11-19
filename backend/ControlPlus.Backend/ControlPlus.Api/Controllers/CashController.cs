using ControlPlus.Api.Authorization;
using ControlPlus.Api.Printing;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/cash")]
[Produces("application/json", "application/problem+json")]
public sealed class CashController(
    CurrentActorContextResolver actorContextResolver,
    ICashService cashService, ICashDrawerService drawer,
    ILogger<CashController> logger) : ApiControllerBase(actorContextResolver)
{
    [HttpGet("closure-difference-reasons")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    [ProducesResponseType<IReadOnlyCollection<CashClosureDifferenceReasonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListClosureDifferenceReasons(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.ListClosureDifferenceReasonsAsync(actor, cancellationToken), Ok);
    }

    [HttpPost("closure-difference-reasons/initialize")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    [ProducesResponseType<IReadOnlyCollection<CashClosureDifferenceReasonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InitializeClosureDifferenceReasons(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.InitializeClosureDifferenceReasonsAsync(actor, cancellationToken), Ok);
    }

    [HttpGet("state")]
    [PermissionAuthorize(PermissionCodes.CashOwnRead)]
    [ProducesResponseType<CashStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetState(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.GetStateAsync(actor, cancellationToken), Ok);
    }

    [HttpPost("register")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashRegisterDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfigureRegister(
        [FromBody] ConfigureCashRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await cashService.ConfigureCashRegisterAsync(actor, request, cancellationToken);
        return FromResult(result, cashRegister => CreatedAtAction(nameof(GetState), cashRegister));
    }

    [HttpPut("shift-mode")]
    [PermissionAuthorize(PermissionCodes.CashShiftModeManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateShiftMode(
        [FromBody] UpdateCashShiftModeRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.UpdateShiftModeAsync(actor, request, cancellationToken), Ok);
    }

    [HttpPost("shifts")]
    [PermissionAuthorize(PermissionCodes.CashOwnRead)]
    [Consumes("application/json")]
    [ProducesResponseType<CashShiftDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenShift(
        [FromBody] OpenCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await cashService.OpenShiftAsync(actor, request, cancellationToken);
        return FromResult(result, shift => CreatedAtAction(nameof(GetState), shift));
    }

    [HttpGet("movements")]
    [PermissionAuthorize(PermissionCodes.CashOwnRead)]
    [ProducesResponseType<PagedResult<CashMovementDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMovements(
        [FromQuery] CashMovementListQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.ListMovementsAsync(actor, query, cancellationToken), Ok);
    }

    [HttpPost("movements/incomes")]
    [PermissionAuthorize(PermissionCodes.CashMovementsManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashMovementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterIncome(
        [FromBody] CreateCashMovementRequest request,
        CancellationToken cancellationToken) =>
        await RegisterMovementAsync(request, cashService.RegisterIncomeAsync, cancellationToken);

    [HttpPost("movements/expenses")]
    [PermissionAuthorize(PermissionCodes.CashMovementsManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashMovementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterExpense(
        [FromBody] CreateCashMovementRequest request,
        CancellationToken cancellationToken) =>
        await RegisterMovementAsync(request, cashService.RegisterExpenseAsync, cancellationToken);

    [HttpPost("movements/cash-drops")]
    [PermissionAuthorize(PermissionCodes.CashMovementsManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashMovementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterCashDrop(
        [FromBody] CreateCashMovementRequest request,
        CancellationToken cancellationToken) =>
        await RegisterMovementAsync(request, cashService.RegisterCashDropAsync, cancellationToken);

    [HttpGet("reconciliation")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    [ProducesResponseType<CashReconciliationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetReconciliation(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.GetReconciliationAsync(actor, cancellationToken), Ok);
    }

    [HttpPost("shifts/{shiftId:guid}/close")]
    [PermissionAuthorize(PermissionCodes.CashShiftManage)]
    [Consumes("application/json")]
    [ProducesResponseType<CashReconciliationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CloseShift(
        Guid shiftId,
        [FromBody] CloseCashShiftRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.CloseShiftAsync(actor, shiftId, request, cancellationToken), Ok);
    }

    [HttpGet("operator-credentials/{userId:guid}")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<OperatorCredentialDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOperatorCredential(Guid userId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() :
            FromResult(await cashService.GetOperatorCredentialAsync(actor, userId, cancellationToken), value => Ok(value));
    }

    [HttpDelete("operator-credentials/{userId:guid}")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeOperatorCredential(
        Guid userId, [FromQuery] Guid credentialId, [FromQuery] long expectedVersion, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() :
            FromResult(await cashService.RevokeOperatorCredentialAsync(actor, userId, credentialId, expectedVersion, cancellationToken));
    }

    [HttpPost("operator-credentials/{userId:guid}")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [Consumes("application/json")]
    [ProducesResponseType<IssuedOperatorCredentialDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueOperatorCredential(
        Guid userId,
        [FromBody] IssueOperatorCredentialRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await cashService.IssueOperatorCredentialAsync(actor, userId, request, cancellationToken);
        return FromResult(result, credential => StatusCode(StatusCodes.Status201Created, credential));
    }

    [HttpPost("operator-sessions")]
    [PermissionAuthorize(PermissionCodes.CashOwnRead)]
    [Consumes("application/json")]
    [ProducesResponseType<OperatorSessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartOperatorSession(
        [FromBody] StartOperatorSessionRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await cashService.StartOperatorSessionAsync(actor, request, cancellationToken);
        return FromResult(result, session => StatusCode(StatusCodes.Status201Created, session));
    }

    [HttpPost("operator-sessions/{sessionId:guid}/close")]
    [PermissionAuthorize(PermissionCodes.CashOwnRead)]
    [ProducesResponseType<OperatorSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CloseOperatorSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await cashService.CloseOperatorSessionAsync(actor, sessionId, cancellationToken), Ok);
    }

    private async Task<IActionResult> RegisterMovementAsync(
        CreateCashMovementRequest request,
        Func<ActorContext, CreateCashMovementRequest, CancellationToken, Task<Result<CashMovementDto>>> operation,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await operation(actor, request, cancellationToken);
        if (result.IsSuccess)
            await CashDrawerControllerDispatch.TryOpenAsync(drawer, actor,
                CashDrawerOperationKind.CashMovement, result.Value!.Id,
                Response, logger, cancellationToken);
        return FromResult(result, movement => StatusCode(StatusCodes.Status201Created, movement));
    }
}
