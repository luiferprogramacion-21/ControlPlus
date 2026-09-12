using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/roles")]
public sealed class RolesController(
    CurrentActorContextResolver actorContextResolver,
    IRoleManagementService roleManagementService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.RolesRead)]
    public async Task<IActionResult> List([FromQuery] RoleListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.ListRolesAsync(actor, query, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpGet("{roleId:guid}")]
    [PermissionAuthorize(PermissionCodes.RolesRead)]
    public async Task<IActionResult> GetById(Guid roleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.GetRoleByIdAsync(actor, roleId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.RolesManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.CreateRoleAsync(actor, request, cancellationToken);
        return FromResult(result, role => CreatedAtAction(nameof(GetById), new { roleId = role.Id }, role));
    }

    [HttpPut("{roleId:guid}")]
    [PermissionAuthorize(PermissionCodes.RolesManage)]
    public async Task<IActionResult> Update(
        Guid roleId,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.UpdateRoleAsync(actor, roleId, request, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{roleId:guid}/activate")]
    [PermissionAuthorize(PermissionCodes.RolesManage)]
    public async Task<IActionResult> Activate(Guid roleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.ActivateRoleAsync(actor, roleId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{roleId:guid}/deactivate")]
    [PermissionAuthorize(PermissionCodes.RolesManage)]
    public async Task<IActionResult> Deactivate(Guid roleId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.DeactivateRoleAsync(actor, roleId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPost("{roleId:guid}/permissions/{permissionId:guid}")]
    [PermissionAuthorize(PermissionCodes.RolesManage)]
    public async Task<IActionResult> GrantPermission(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.GrantPermissionAsync(actor, roleId, permissionId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpDelete("{roleId:guid}/permissions/{permissionId:guid}")]
    [PermissionAuthorize(PermissionCodes.RolesManage)]
    public async Task<IActionResult> RevokePermission(
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await roleManagementService.RevokePermissionAsync(actor, roleId, permissionId, cancellationToken);
        return FromResult(result, Ok);
    }
}
