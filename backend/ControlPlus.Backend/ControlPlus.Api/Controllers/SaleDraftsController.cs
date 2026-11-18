using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

/// <summary>Venta recuperable: el carrito en curso del usuario en el turno abierto (decisión del 18 de noviembre de 2026).</summary>
[Route("api/sales/drafts/current")]
public sealed class SaleDraftsController(
    CurrentActorContextResolver actorContextResolver,
    ISaleDraftService drafts) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Borrador por recuperar; 204 si no hay ninguno o no hay turno abierto.</summary>
    [HttpGet]
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

    /// <summary>Guarda el carrito en curso, reemplazando el borrador anterior. 409 si no hay turno abierto.</summary>
    [HttpPut]
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

    /// <summary>Descarta el borrador en curso (por ejemplo, al vaciar o cancelar el carrito).</summary>
    [HttpDelete]
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
}
