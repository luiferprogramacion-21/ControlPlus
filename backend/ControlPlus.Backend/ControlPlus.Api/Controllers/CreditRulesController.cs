using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/sales")]
public sealed class CreditRulesController(
    CurrentActorContextResolver actorContextResolver,
    ICreditSettingsService settings) : ApiControllerBase(actorContextResolver)
{
    /// <summary>Consulta el plazo sugerido que necesita quien prepara una venta a crédito.</summary>
    [HttpGet("credit-rules")]
    [PermissionAuthorize(PermissionCodes.CreditRequest)]
    [ProducesResponseType<CreditRulesDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.GetSalesRulesAsync(actor, cancellationToken), Ok);
    }
}
