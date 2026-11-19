using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/sales/{saleId:guid}/changes/{changeId:guid}/receipt-requests")]
public sealed class SaleCorrectionReceiptsController(CurrentActorContextResolver actorContextResolver,
    ISalesService salesService) : ApiControllerBase(actorContextResolver)
{
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
