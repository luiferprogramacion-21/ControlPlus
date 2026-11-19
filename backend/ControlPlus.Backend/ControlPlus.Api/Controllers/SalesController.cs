using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Printing;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/sales")]
public sealed class SalesController(
    CurrentActorContextResolver actorContextResolver,
    ISalesService salesService,
    IReceiptPrintingService printing,
    ICashDrawerService drawer,
    ILogger<SalesController> logger) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Confirma una venta CONTADO o CREDITO idempotente y descuenta su inventario.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [Consumes("application/json")]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Confirm(
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] ConfirmSaleRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await salesService.ConfirmAsync(actor, idempotencyKey, request, cancellationToken);
        if (result.IsSuccess)
        {
            if (result.Value!.Payments.Any(x => x.MethodCode == "EFECTIVO" && x.AppliedAmount > 0))
                await CashDrawerControllerDispatch.TryOpenAsync(drawer, actor,
                    CashDrawerOperationKind.Sale, result.Value.Id, Response, logger, cancellationToken);
            try { await printing.QueueOriginalAsync(result.Value!.Id, cancellationToken); }
            catch (Exception exception)
            {
                logger.LogError(exception, "No se pudo preparar el comprobante de venta {SaleId}.", result.Value!.Id);
            }
        }
        return FromResult(result, sale => CreatedAtAction(nameof(Get), new { saleId = sale.Id }, sale));
    }

    /// <summary>Consulta las ventas del usuario autenticado.</summary>
    [HttpGet("own")]
    [PermissionAuthorize(PermissionCodes.SalesOwnRead)]
    [ProducesResponseType<PagedResult<SaleSummaryDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ListOwn([FromQuery] SaleListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.ListOwnAsync(actor, query, cancellationToken), Ok);
    }

    /// <summary>Consulta el historial general de ventas.</summary>
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.SalesAllRead)]
    [ProducesResponseType<PagedResult<SaleSummaryDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ListAll([FromQuery] SaleListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.ListAllAsync(actor, query, cancellationToken), Ok);
    }

    /// <summary>Verifica, al aplicar un descuento o elegir precio mayorista, las credenciales de un Supervisor o Administrador. No registra nada: la venta las valida de nuevo.</summary>
    [HttpPost("concession-authorizations/verify")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [Consumes("application/json")]
    [ProducesResponseType<ConcessionAuthorizationDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> VerifyConcessionAuthorization(
        [FromBody] VerifyConcessionAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.VerifyConcessionAuthorizationAsync(
            actor, request, cancellationToken), Ok);
    }

    /// <summary>Consulta el plazo para futuras ventas; las fechas límite históricas no cambian.</summary>
    [HttpGet("change-policy")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [ProducesResponseType<SaleChangePolicyDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetChangePolicy(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.GetChangePolicyAsync(actor,
            cancellationToken), Ok);
    }

    /// <summary>Configura el plazo de cambio para ventas nuevas, con versión y auditoría.</summary>
    [HttpPut("change-policy")]
    [PermissionAuthorize(PermissionCodes.ConfigurationManage)]
    [Consumes("application/json")]
    [ProducesResponseType<SaleChangePolicyDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> UpdateChangePolicy([FromBody] UpdateSaleChangePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.UpdateChangePolicyAsync(actor,
            request, cancellationToken), Ok);
    }

    /// <summary>Consulta una venta si pertenece al actor o si este puede leer el historial general.</summary>
    [HttpGet("{saleId:guid}")]
    [Authorize]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid saleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.GetAsync(actor, saleId, cancellationToken), Ok);
    }

    /// <summary>Busca por número visible una venta para preparar un cambio de productos.</summary>
    [HttpGet("change-search")]
    [Authorize]
    [ProducesResponseType<SaleChangePreparationDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> FindChangeSale([FromQuery, Required] string visibleNumber,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.FindChangeSaleAsync(actor,
            visibleNumber, cancellationToken), Ok);
    }

    /// <summary>Consulta el plazo y las cantidades pendientes de devolución de una venta.</summary>
    [HttpGet("{saleId:guid}/change-preparation")]
    [Authorize]
    [ProducesResponseType<SaleChangePreparationDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetChangePreparation(Guid saleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.GetChangePreparationAsync(actor,
            saleId, cancellationToken), Ok);
    }

    /// <summary>Consulta el historial confirmado de cambios de productos de una venta.</summary>
    [HttpGet("{saleId:guid}/changes")]
    [Authorize]
    [ProducesResponseType<IReadOnlyCollection<SaleChangeDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> ListChanges(Guid saleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.ListChangesAsync(actor,
            saleId, cancellationToken), Ok);
    }

    /// <summary>Confirma un cambio autorizado con inventario, Caja y crédito atómicos.</summary>
    [HttpPost("{saleId:guid}/changes")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [Consumes("application/json")]
    [ProducesResponseType<SaleChangeDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> ConfirmChange(Guid saleId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] ConfirmSaleChangeRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await salesService.ConfirmChangeAsync(actor, saleId, idempotencyKey,
            request, cancellationToken);
        if (result.IsSuccess && result.Value!.Payments.Any(x =>
                x.MethodCode == "EFECTIVO" && x.Amount > 0))
            await CashDrawerControllerDispatch.TryOpenAsync(drawer, actor,
                CashDrawerOperationKind.SaleChange, result.Value.Id,
                Response, logger, cancellationToken);
        return FromResult(result, change => CreatedAtAction(nameof(ListChanges), new { saleId }, change));
    }

    /// <summary>Obtiene los datos históricos del comprobante de una venta confirmada.</summary>
    [HttpGet("{saleId:guid}/receipt")]
    [Authorize]
    [ProducesResponseType<SaleReceiptDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> GetReceipt(Guid saleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.GetReceiptAsync(actor, saleId, cancellationToken), Ok);
    }

    /// <summary>Consulta el estado de cola de las copias del comprobante, sin afirmar salida en papel.</summary>
    [HttpGet("{saleId:guid}/print-jobs")]
    [Authorize]
    [ProducesResponseType<PagedResult<PrintJobDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> ListPrintJobs(Guid saleId, [FromQuery] PrintJobsQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await printing.ListSaleJobsAsync(actor, saleId,
            query, cancellationToken), Ok);
    }

    /// <summary>Reintenta un trabajo fallido de esta venta; repetirlo no crea otra copia.</summary>
    [HttpPost("{saleId:guid}/print-jobs/{jobId:guid}/retry")]
    [PermissionAuthorize(PermissionCodes.SalesReprint)]
    [ProducesResponseType<PrintJobDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> RetryPrintJob(Guid saleId, Guid jobId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await printing.RetrySaleJobAsync(actor, saleId,
            jobId, cancellationToken), Ok);
    }

    /// <summary>Registra una solicitud de reimpresión; no confirma impresión física.</summary>
    [HttpPost("{saleId:guid}/reprint-requests")]
    [PermissionAuthorize(PermissionCodes.SalesReprint)]
    [ProducesResponseType<SaleReprintRequestDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> RequestReprint(Guid saleId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await salesService.RequestReprintAsync(actor, saleId, idempotencyKey, cancellationToken);
        if (result.IsSuccess)
        {
            try { await printing.QueueReprintAsync(result.Value!.RequestId, saleId, actor.UserId, cancellationToken); }
            catch (Exception exception)
            {
                logger.LogError(exception, "No se pudo preparar la reimpresión de la venta {SaleId}.", saleId);
            }
        }
        return FromResult(result, Ok);
    }

    /// <summary>Consulta los motivos activos y aprobados para anular ventas.</summary>
    [HttpGet("cancellation-reasons")]
    [PermissionAuthorize(PermissionCodes.SalesVoid)]
    [ProducesResponseType<IReadOnlyCollection<SaleCancellationReasonDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> ListCancellationReasons(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.ListCancellationReasonsAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Inicializa explícitamente los tres motivos de anulación aprobados.</summary>
    [HttpPost("cancellation-reasons/initialize")]
    [PermissionAuthorize(PermissionCodes.SalesVoid)]
    [ProducesResponseType<IReadOnlyCollection<SaleCancellationReasonDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> InitializeCancellationReasons(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.InitializeCancellationReasonsAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Anula una venta con autorización y reversión atómica de sus efectos.</summary>
    [HttpPost("{saleId:guid}/cancel")]
    [PermissionAuthorize(PermissionCodes.SalesVoid)]
    [Consumes("application/json")]
    [ProducesResponseType<CancelSaleDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Cancel(Guid saleId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromBody] CancelSaleRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.CancelAsync(actor, saleId,
            idempotencyKey, request, cancellationToken), Ok);
    }
}
