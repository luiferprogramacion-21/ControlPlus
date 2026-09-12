using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/audit-records")]
public sealed class AuditRecordsController(
    CurrentActorContextResolver actorContextResolver,
    IAuditQueryService auditQueryService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.AuditRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Query([FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await auditQueryService.QueryAsync(actor, query, cancellationToken);
        return FromResult(result, Ok);
    }
}
