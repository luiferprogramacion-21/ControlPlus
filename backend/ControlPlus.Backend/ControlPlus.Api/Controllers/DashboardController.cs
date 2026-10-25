using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/dashboard")]
public sealed class DashboardController(CurrentActorContextResolver actorContextResolver,
    IDashboardSummaryService service) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Resumen de ventas y flujo de Caja para la fecha operativa del establecimiento activo.</summary>
    [HttpGet("summary")]
    [PermissionAuthorize(PermissionCodes.FinancialReportsRead)]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await service.GetAsync(actor, cancellationToken), Ok);
    }
}
