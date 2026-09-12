using ControlPlus.Application.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Security.Contracts;

public sealed record CreateRoleRequest(string Code, string Name, RoleLevel Level);

public sealed record UpdateRoleRequest(string Name, RoleLevel Level);

public sealed record CreatePermissionRequest(string Code, string Name, string? Description, string Module);

public sealed record UpdatePermissionRequest(string Name, string? Description);

public sealed record RoleDto(
    Guid Id,
    string Code,
    string Name,
    RoleLevel Level,
    bool IsActive,
    IReadOnlyCollection<PermissionDto> Permissions);

public sealed record PermissionDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Module,
    bool IsActive);

public sealed record RoleListQuery(
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 50);

public sealed record PermissionListQuery(
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 100);

public interface IRoleManagementService
{
    Task<Result<RoleDto>> CreateRoleAsync(ActorContext actor, CreateRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> UpdateRoleAsync(ActorContext actor, Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> ActivateRoleAsync(ActorContext actor, Guid roleId, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> DeactivateRoleAsync(ActorContext actor, Guid roleId, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> GrantPermissionAsync(ActorContext actor, Guid roleId, Guid permissionId, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> RevokePermissionAsync(ActorContext actor, Guid roleId, Guid permissionId, CancellationToken cancellationToken = default);

    Task<Result<PermissionDto>> CreatePermissionAsync(ActorContext actor, CreatePermissionRequest request, CancellationToken cancellationToken = default);

    Task<Result<PermissionDto>> UpdatePermissionAsync(ActorContext actor, Guid permissionId, UpdatePermissionRequest request, CancellationToken cancellationToken = default);

    Task<Result<PermissionDto>> ActivatePermissionAsync(ActorContext actor, Guid permissionId, CancellationToken cancellationToken = default);

    Task<Result<PermissionDto>> DeactivatePermissionAsync(ActorContext actor, Guid permissionId, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> GetRoleByIdAsync(ActorContext actor, Guid roleId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<RoleDto>>> ListRolesAsync(ActorContext actor, RoleListQuery query, CancellationToken cancellationToken = default);

    Task<Result<PermissionDto>> GetPermissionByIdAsync(ActorContext actor, Guid permissionId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<PermissionDto>>> ListPermissionsAsync(ActorContext actor, PermissionListQuery query, CancellationToken cancellationToken = default);
}
