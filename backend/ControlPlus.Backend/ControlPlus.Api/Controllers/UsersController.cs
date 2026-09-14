using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[Route("api/users")]
public sealed class UsersController(
    CurrentActorContextResolver actorContextResolver,
    IUserManagementService userManagementService) : ApiControllerBase(actorContextResolver)
{
    [HttpGet]
    [PermissionAuthorize(PermissionCodes.UsersRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] UserListQuery query, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.ListAsync(actor, query, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpGet("{userId:guid}")]
    [PermissionAuthorize(PermissionCodes.UsersRead)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.GetByIdAsync(actor, userId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPost]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.CreateAsync(actor, request, cancellationToken);
        return FromResult(result, user => CreatedAtAction(nameof(GetById), new { userId = user.Id }, user));
    }

    [HttpPut("{userId:guid}")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.UpdateAsync(actor, userId, request, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{userId:guid}/activate")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.ActivateAsync(actor, userId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{userId:guid}/deactivate")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.DeactivateAsync(actor, userId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPost("{userId:guid}/reactivate")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.ReactivateAsync(actor, userId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{userId:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(
        Guid userId,
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.ChangePasswordAsync(actor, userId, request, cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{userId:guid}/roles")]
    [PermissionAuthorize(PermissionCodes.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignRole(
        Guid userId,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var result = await userManagementService.AssignRoleAsync(actor, userId, request, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpGet("{userId:guid}/permissions/effective")]
    [PermissionAuthorize(PermissionCodes.UserPermissionsManage)]
    public async Task<IActionResult> GetEffectivePermissions(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        var result = await userManagementService.GetEffectivePermissionsAsync(actor, userId, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPut("{userId:guid}/permissions/{permissionId:guid}")]
    [PermissionAuthorize(PermissionCodes.UserPermissionsManage)]
    public async Task<IActionResult> SetPermissionOverride(
        Guid userId,
        Guid permissionId,
        [FromBody] SetUserPermissionRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await userManagementService.SetPermissionOverrideAsync(
            actor, userId, permissionId, request, cancellationToken));
    }

    [HttpDelete("{userId:guid}/permissions")]
    [PermissionAuthorize(PermissionCodes.UserPermissionsManage)]
    public async Task<IActionResult> ResetPermissionOverrides(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return MissingActor();
        return FromResult(await userManagementService.ResetPermissionOverridesAsync(actor, userId, cancellationToken));
    }
}
