using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/products")]
public sealed class ProductsController(
    CurrentActorContextResolver actorContextResolver,
    ICatalogService catalogService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.ProductsRead)]
    public async Task<IActionResult> List([FromQuery] ProductListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.ListProductsAsync(actor, query, cancellationToken), Ok);
    }

    [HttpGet("{productId:guid}")]
    [PermissionAuthorize(PermissionCodes.ProductsRead)]
    public async Task<IActionResult> Get(Guid productId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.GetProductAsync(actor, productId, cancellationToken), Ok);
    }

    [HttpGet("{productId:guid}/purchase-cost-history")]
    [PermissionAuthorize(PermissionCodes.ProductCostsRead)]
    public async Task<IActionResult> GetPurchaseCostHistory(
        Guid productId,
        [FromQuery] ProductPurchaseCostHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await catalogService.GetProductPurchaseCostHistoryAsync(actor, productId, query, cancellationToken), Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.ProductsCreate)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await catalogService.CreateProductAsync(actor, request, cancellationToken);
        return FromResult(result, x => CreatedAtAction(nameof(Get), new { productId = x.Id }, x));
    }

    [HttpPut("{productId:guid}")]
    [PermissionAuthorize(PermissionCodes.ProductsSensitiveUpdate)]
    public async Task<IActionResult> Update(Guid productId, [FromBody] UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.UpdateProductAsync(actor, productId, request, cancellationToken), Ok);
    }

    [HttpPut("{productId:guid}/activate")]
    [PermissionAuthorize(PermissionCodes.ProductsSensitiveUpdate)]
    public async Task<IActionResult> Activate(Guid productId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.SetProductActiveAsync(actor, productId, true, cancellationToken), Ok);
    }

    [HttpPut("{productId:guid}/deactivate")]
    [PermissionAuthorize(PermissionCodes.ProductsSensitiveUpdate)]
    public async Task<IActionResult> Deactivate(Guid productId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.SetProductActiveAsync(actor, productId, false, cancellationToken), Ok);
    }
}

[Route("api/measurement-units")]
public sealed class MeasurementUnitsController(
    CurrentActorContextResolver actorContextResolver,
    ICatalogService catalogService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.ProductsRead)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.ListMeasurementUnitsAsync(actor, cancellationToken), Ok);
    }
}
