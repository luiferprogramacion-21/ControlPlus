using System.ComponentModel.DataAnnotations;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

/// <summary>
/// Correcciones de apartados (decisión del 19 de noviembre de 2026): anular un abono equivocado sin cancelar el
/// apartado y corregir sus productos antes de entregarlo.
/// </summary>
[Route("api/layaways")]
[PermissionAuthorize(PermissionCodes.LayawayManage)]
public sealed class LayawayPaymentCorrectionsController(CurrentActorContextResolver resolver,
    ILayawayPaymentCancellationService service, ILayawayProductCorrectionService products) : ApiControllerBase(resolver)
{
    [HttpGet("payment-cancellation-reasons")]
    public async Task<IActionResult> Reasons(CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await service.ReasonsAsync(actor, ct), Ok); }

    [HttpPost("{layawayId:guid}/payments/{paymentId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid layawayId, Guid paymentId,
        [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] CancelLayawayPaymentRequest request, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await service.CancelAsync(actor, layawayId, paymentId, key, request, ct), Ok); }

    /// <summary>Correcciones de productos registradas en el apartado, de la más antigua a la más reciente.</summary>
    [HttpGet("{layawayId:guid}/product-corrections")]
    public async Task<IActionResult> ProductCorrections(Guid layawayId, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await products.HistoryAsync(actor, layawayId, ct), Ok); }

    /// <summary>Reemplaza los productos de un apartado sin entregar; ajusta reservas, saldo y la devolución si sobra dinero.</summary>
    [HttpPost("{layawayId:guid}/product-corrections")]
    public async Task<IActionResult> CorrectProducts(Guid layawayId,
        [FromHeader(Name = "Idempotency-Key"), Required] string key,
        [FromBody] CorrectLayawayProductsRequest request, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await products.CorrectAsync(actor, layawayId, key, request, ct), Ok); }
}
