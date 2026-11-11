using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/printing")]
public sealed class PrintingController(CurrentActorContextResolver actorResolver,
    IReceiptPrintingService printing) : ApiControllerBase(actorResolver)
{
    [HttpPost("profiles")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<PrintProfileDto>(StatusCodes.Status201Created, "application/json")]
    public async Task<IActionResult> CreateProfile([FromBody] CreatePrintProfileRequest request,
        CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await printing.CreateProfileAsync(actor, request, ct),
            profile => StatusCode(StatusCodes.Status201Created, profile));
    }

    [HttpGet("profiles")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<IReadOnlyCollection<PrintProfileDto>>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> ListProfiles(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await printing.ListProfilesAsync(actor, ct), Ok);
    }

    /// <summary>Indica si la terminal tiene un perfil de recibos listo, sin revelar la cola de Windows.</summary>
    [HttpGet("status")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType<PrintStatusDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await printing.GetStatusAsync(actor, ct), Ok);
    }

    [HttpGet("jobs")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<ControlPlus.Application.Common.PagedResult<PrintJobDto>>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> ListJobs([FromQuery] PrintJobsQuery query, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await printing.ListJobsAsync(actor, query, ct), Ok);
    }

    [HttpPost("jobs/{jobId:guid}/retry")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<PrintJobDto>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Retry(Guid jobId, CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? MissingActor() : FromResult(await printing.RetryAsync(actor, jobId, ct), Ok);
    }
}
