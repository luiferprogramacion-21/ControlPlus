using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/sales/{saleId:guid}/changes/{changeId:guid}/receipt-requests")]
public sealed class SaleCorrectionReceiptsController(CurrentActorContextResolver actorContextResolver,
    ISalesService salesService, IReceiptPrintingService printing) : ApiControllerBase(actorContextResolver)
{
    /// <summary>
    /// Prepara el comprobante y lo manda a la impresora de tickets, como el ticket de venta (pruebas del usuario del
    /// 26/11/2026). Sin impresora de tickets responde SIN_PERFIL y la pantalla lo imprime en el navegador.
    /// </summary>
    [HttpPost("print")]
    [PermissionAuthorize(PermissionCodes.SalesReprint)]
    [ProducesResponseType<SaleCorrectionPrintDto>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Print(Guid saleId, Guid changeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var prepared = await salesService.PrepareCorrectionReceiptAsync(actor, saleId, changeId, idempotencyKey, cancellationToken);
        if (prepared.IsFailure) return FromResult(prepared, receipt => Ok(receipt));
        var printed = await printing.PrintCorrectionAsync(actor.UserId, prepared.Value!, cancellationToken);
        return Ok(new SaleCorrectionPrintDto(prepared.Value!, printed.Status, printed.Message));
    }

    /// <summary>Prepara y audita un comprobante de corrección para imprimir en el navegador; no envía órdenes al hardware.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.SalesReprint)]
    [ProducesResponseType<SaleCorrectionReceiptDto>(StatusCodes.Status201Created, "application/json")]
    public async Task<IActionResult> Prepare(Guid saleId, Guid changeId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await salesService.PrepareCorrectionReceiptAsync(
            actor, saleId, changeId, idempotencyKey, cancellationToken), receipt => StatusCode(StatusCodes.Status201Created, receipt));
    }
}
