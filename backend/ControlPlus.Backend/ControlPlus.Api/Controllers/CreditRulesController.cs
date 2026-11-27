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

    /// <summary>
    /// Situación de crédito de un cliente para una venta a crédito de <paramref name="amount"/> pesos: saldo pendiente y
    /// vencido, cupo y si la venta quedaría bloqueada (exige autorización de un Administrador). Solo lectura.
    /// </summary>
    [HttpGet("credit-check")]
    [PermissionAuthorize(PermissionCodes.CreditRequest)]
    [ProducesResponseType<CustomerCreditRiskDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Check([FromQuery] Guid customerId, [FromQuery] decimal amount, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.GetCustomerRiskAsync(actor, customerId, amount, cancellationToken), Ok);
    }

    /// <summary>Resumen de los clientes que deben, morosos y por vencer (pruebas del usuario del 26/11/2026). Solo lectura.</summary>
    [HttpGet("customer-credit-summary")]
    [PermissionAuthorize(PermissionCodes.CreditRequest)]
    [ProducesResponseType<CustomerCreditSummaryDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.GetCustomerSummaryAsync(actor, cancellationToken), Ok);
    }

    /// <summary>
    /// Etapa del crédito (SIN_DEUDA, AL_DIA, POR_VENCER o VENCIDO) de hasta 100 clientes, para mostrarla con su color en
    /// Clientes (auditoría C.14). Solo lectura.
    /// </summary>
    [HttpGet("customer-credit-stages")]
    [PermissionAuthorize(PermissionCodes.CreditRequest)]
    [ProducesResponseType<IReadOnlyCollection<CustomerCreditStageDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Stages([FromQuery] Guid[] customerIds, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null
            ? MissingActor()
            : FromResult(await settings.GetCustomerStagesAsync(actor, customerIds, cancellationToken), Ok);
    }
}
