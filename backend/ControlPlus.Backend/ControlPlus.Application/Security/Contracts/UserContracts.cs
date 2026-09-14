using ControlPlus.Application.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Security.Contracts;

public sealed record CreateUserRequest(
    string UserName,
    string DisplayName,
    string Password,
    IReadOnlyCollection<Guid> RoleIds);

public sealed record UpdateUserRequest(string DisplayName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AssignRoleRequest(Guid RoleId);

public sealed record SetUserPermissionRequest(bool Granted);

public sealed record EffectiveUserPermissionDto(
    Guid Id,
    string Code,
    string Name,
    bool Granted,
    string Source,
    string? IndividualEffect);

public sealed record UserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    bool IsLocked,
    int FailedLoginAttempts,
    DateTimeOffset? LockedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    IReadOnlyCollection<RoleSummaryDto> Roles);

public sealed record RoleSummaryDto(Guid Id, string Code, string Name, RoleLevel Level, bool IsActive);

public sealed record UserListQuery(
    string? Search = null,
    bool? IsActive = null,
    bool? IsLocked = null,
    int Page = 1,
    int PageSize = 50);

public interface IUserManagementService
{
    Task<Result<UserDto>> CreateAsync(ActorContext actor, CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> UpdateAsync(ActorContext actor, Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> ActivateAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> DeactivateAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> ReactivateAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(ActorContext actor, Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> AssignRoleAsync(ActorContext actor, Guid userId, AssignRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> GetByIdAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<UserDto>>> ListAsync(ActorContext actor, UserListQuery query, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<EffectiveUserPermissionDto>>> GetEffectivePermissionsAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);

    Task<Result> SetPermissionOverrideAsync(ActorContext actor, Guid userId, Guid permissionId, SetUserPermissionRequest request, CancellationToken cancellationToken = default);

    Task<Result> ResetPermissionOverridesAsync(ActorContext actor, Guid userId, CancellationToken cancellationToken = default);
}
