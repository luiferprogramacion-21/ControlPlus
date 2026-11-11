using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/reports")]
public sealed class ReportsController(CurrentActorContextResolver actorContextResolver, IFinancialSalesReportService service,
    IDailyCashFlowService cashFlowService, ISalesOverviewService overviewService)
    : ApiControllerBase(actorContextResolver)
{
    /// <summary>Ventas confirmadas por fecha operativa de la instalación activa; rango inclusivo de hasta 31 días.</summary>
    [HttpGet("sales/daily")]
    [PermissionAuthorize(PermissionCodes.FinancialReportsRead)]
    [ProducesResponseType<FinancialSalesReport>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetDailySales(
        [FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await service.GetAsync(actor, from, to, cancellationToken), Ok);
    }

    /// <summary>Cobros, reembolsos y movimientos manuales por fecha operativa del turno.</summary>
    [HttpGet("cash-flow/daily")]
    [PermissionAuthorize(PermissionCodes.FinancialReportsRead)]
    [ProducesResponseType<DailyCashFlowReport>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetDailyCashFlow(
        [FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await cashFlowService.GetAsync(actor, from, to, cancellationToken), Ok);
    }

    /// <summary>Ventas, cambios, margen comercial estimado y medios de cobro inmediato.</summary>
    [HttpGet("sales/overview")]
    [PermissionAuthorize(PermissionCodes.FinancialReportsRead)]
    [ProducesResponseType<SalesOverviewReport>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetSalesOverview([FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await overviewService.GetAsync(actor, from, to, cancellationToken), Ok);
    }

    /// <summary>Descarga PDF del mismo resumen financiero de ventas.</summary>
    [HttpGet("sales/overview.pdf")]
    [PermissionAuthorize(PermissionCodes.FinancialReportsRead)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DownloadSalesOverview([FromQuery, Required] DateOnly? from,
        [FromQuery, Required] DateOnly? to, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await overviewService.GetAsync(actor, from, to, cancellationToken), report =>
            File(SalesOverviewPdf.Render(report), "application/pdf",
                $"reporte-ventas-{report.From:yyyy-MM-dd}_{report.To:yyyy-MM-dd}.pdf"));
    }
}
