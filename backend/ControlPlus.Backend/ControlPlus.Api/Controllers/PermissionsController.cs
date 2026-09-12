using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/permissions")]
public sealed class PermissionsController(
    CurrentActorContextResolver actorContextResolver,
    IRoleManagementService roleManagementService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.PermissionsRead)]
    public async Task<IActionResult> List([FromQuery] PermissionListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.ListPermissionsAsync(actor, query, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpGet("{permissionId:guid}")]
    [PermissionAuthorize(PermissionCodes.PermissionsRead)]
    public async Task<IActionResult> GetById(Guid permissionId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.GetPermissionByIdAsync(actor, permissionId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.PermissionsManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.CreatePermissionAsync(actor, request, cancellationToken);
        return FromResult(result, permission => CreatedAtAction(
            nameof(GetById),
            new { permissionId = permission.Id },
            permission));
    }

    [HttpPut("{permissionId:guid}")]
    [PermissionAuthorize(PermissionCodes.PermissionsManage)]
    public async Task<IActionResult> Update(
        Guid permissionId,
        [FromBody] UpdatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.UpdatePermissionAsync(actor, permissionId, request, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{permissionId:guid}/activate")]
    [PermissionAuthorize(PermissionCodes.PermissionsManage)]
    public async Task<IActionResult> Activate(Guid permissionId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.ActivatePermissionAsync(actor, permissionId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{permissionId:guid}/deactivate")]
    [PermissionAuthorize(PermissionCodes.PermissionsManage)]
    public async Task<IActionResult> Deactivate(Guid permissionId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.DeactivatePermissionAsync(actor, permissionId, cancellationToken);
        return FromResult(result, Ok);
    }
}
