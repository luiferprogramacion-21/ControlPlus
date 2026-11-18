using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/product-labels")]
[PermissionAuthorize(PermissionCodes.ProductLabelsManage)]
public sealed class ProductLabelsController(
    CurrentActorContextResolver actorResolver,
    IProductLabelPrintingService labels) : ApiControllerBase(actorResolver)
{
    [HttpGet("profiles")]
    [ProducesResponseType<IReadOnlyCollection<ProductLabelProfileDto>>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> ListProfiles(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() :
            FromResult(await labels.ListProfilesAsync(actor, cancellationToken), Ok);
    }

    [HttpGet("products")]
    [ProducesResponseType<PagedResult<ProductLabelProductDto>>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> ListProducts([FromQuery] ProductLabelProductsQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() :
            FromResult(await labels.ListProductsAsync(actor, query, cancellationToken), Ok);
    }

    [HttpPost("jobs")]
    [ProducesResponseType<ProductLabelJobDto>(StatusCodes.Status202Accepted, "application/json")]
    public async Task<IActionResult> Queue(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CreateProductLabelJobRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() :
            FromResult(await labels.QueueAsync(actor, idempotencyKey, request, cancellationToken),
                job => Accepted(job));
    }
}
