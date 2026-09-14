using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/categories")]
public sealed class CategoriesController(
    CurrentActorContextResolver actorContextResolver,
    ICatalogService catalogService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.CategoriesRead)]
    [ProducesResponseType<PagedResult<CategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] CategoryListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.ListCategoriesAsync(actor, query, cancellationToken), Ok);
    }

    [HttpGet("{categoryId:guid}")]
    [PermissionAuthorize(PermissionCodes.CategoriesRead)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid categoryId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.GetCategoryAsync(actor, categoryId, cancellationToken), Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.CategoriesManage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await catalogService.CreateCategoryAsync(actor, request, cancellationToken);
        return FromResult(result, x => CreatedAtAction(nameof(Get), new { categoryId = x.Id }, x));
    }

    [HttpPut("{categoryId:guid}")]
    [PermissionAuthorize(PermissionCodes.CategoriesManage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid categoryId, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.UpdateCategoryAsync(actor, categoryId, request, cancellationToken), Ok);
    }

    [HttpPut("{categoryId:guid}/activate")]
    [PermissionAuthorize(PermissionCodes.CategoriesManage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid categoryId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.SetCategoryActiveAsync(actor, categoryId, true, cancellationToken), Ok);
    }

    [HttpPut("{categoryId:guid}/deactivate")]
    [PermissionAuthorize(PermissionCodes.CategoriesManage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid categoryId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await catalogService.SetCategoryActiveAsync(actor, categoryId, false, cancellationToken), Ok);
    }
}
