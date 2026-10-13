using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Suppliers.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/suppliers")]
public sealed class SuppliersController(CurrentActorContextResolver actors, ISupplierService suppliers) : ApiControllerBase(actors)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.SuppliersRead)]
    [ProducesResponseType<PagedResult<SupplierDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List([FromQuery] SupplierListQuery query, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await suppliers.ListAsync(actor, query, ct), Ok); }

    [HttpGet("{supplierId:guid}")]
    [PermissionAuthorize(PermissionCodes.SuppliersRead)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid supplierId, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await suppliers.GetAsync(actor, supplierId, ct), Ok); }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.SuppliersManage)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(CreateSupplierRequest request, CancellationToken ct)
    { var actor = await GetActorAsync(ct); if (actor is null) return MissingActor(); return FromResult(await suppliers.CreateAsync(actor, request, ct), x => CreatedAtAction(nameof(Get), new { supplierId = x.Id }, x)); }

    [HttpPut("{supplierId:guid}")]
    [PermissionAuthorize(PermissionCodes.SuppliersManage)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid supplierId, UpdateSupplierRequest request, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await suppliers.UpdateAsync(actor, supplierId, request, ct), Ok); }

    [HttpPut("{supplierId:guid}/status")]
    [PermissionAuthorize(PermissionCodes.SuppliersManage)]
    [ProducesResponseType<SupplierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> SetStatus(Guid supplierId, SetSupplierStatusRequest request, CancellationToken ct)
    { var actor = await GetActorAsync(ct); return actor is null ? MissingActor() : FromResult(await suppliers.SetStatusAsync(actor, supplierId, request, ct), Ok); }
}
