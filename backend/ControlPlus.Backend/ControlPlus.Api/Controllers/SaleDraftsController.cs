using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

/// <summary>
/// Venta recuperable (decisión del 18 de noviembre de 2026) y ventas en espera (decisión del 19 de noviembre de 2026):
/// los carritos sin terminar del usuario en el turno abierto.
/// </summary>
[Route("api/sales/drafts")]
public sealed class SaleDraftsController(
    CurrentActorContextResolver actorContextResolver,
    ISaleDraftService drafts) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Borrador más reciente por recuperar; 204 si no hay ninguno o no hay turno abierto.</summary>
    [HttpGet("current")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType<SaleDraftDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.GetCurrentAsync(actor, cancellationToken), draft => draft is null ? NoContent() : Ok(draft));
    }

    /// <summary>Guarda el carrito en curso, reemplazando el borrador más reciente. 409 si no hay turno abierto.</summary>
    [HttpPut("current")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType<SaleDraftDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Save([FromBody] SaveSaleDraftRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.SaveAsync(actor, request, cancellationToken), Ok);
    }

    /// <summary>Descarta todos los borradores abiertos del usuario.</summary>
    [HttpDelete("current")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Discard(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.DiscardAsync(actor, cancellationToken));
    }

    /// <summary>Ventas sin terminar del usuario en el turno abierto (la de pantalla y las en espera), con un resumen.</summary>
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType<IReadOnlyCollection<HeldSaleDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.ListAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Guarda un carrito nuevo como otra venta sin terminar. 409 sin turno abierto o con diez abiertas.</summary>
    [HttpPost]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [Consumes("application/json")]
    [ProducesResponseType<SaleDraftDto>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] SaveSaleDraftRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.CreateAsync(actor, request, cancellationToken),
            draft => Created($"/api/sales/drafts/{draft.Id}", draft));
    }

    /// <summary>Una venta sin terminar del usuario, para retomarla. 404 si ya se cerró o es de otro.</summary>
    [HttpGet("{draftId:guid}")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType<SaleDraftDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetById(Guid draftId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.GetAsync(actor, draftId, cancellationToken), Ok);
    }

    /// <summary>Reemplaza una venta sin terminar del usuario. 404 si ya se cerró o es de otro.</summary>
    [HttpPut("{draftId:guid}")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [Consumes("application/json")]
    [ProducesResponseType<SaleDraftDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid draftId, [FromBody] SaveSaleDraftRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.SaveAsync(actor, draftId, request, cancellationToken), Ok);
    }

    /// <summary>Descarta una venta sin terminar del usuario. 404 si ya se cerró o es de otro.</summary>
    [HttpDelete("{draftId:guid}")]
    [PermissionAuthorize(PermissionCodes.SalesCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DiscardById(Guid draftId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await drafts.DiscardAsync(actor, draftId, cancellationToken));
    }
}
