using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.Security;
using User = ControlPlus.Domain.OfficialModel.Usuario;
using Role = ControlPlus.Domain.OfficialModel.Rol;
using Permission = ControlPlus.Domain.OfficialModel.Permiso;

namespace ControlPlus.Application.Security.Services;

internal static class SecurityMappings
{
    public static UserDto ToDto(User user) =>
        new(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.IsActive,
            user.IsLocked,
            user.FailedLoginAttempts,
            user.LockedAtUtc,
            user.LastLoginAtUtc,
            user.UserRoles
                .Select(userRole => ToSummaryDto(userRole.Role))
                .OrderBy(role => role.Level)
                .ThenBy(role => role.Code, StringComparer.Ordinal)
                .ToArray());

    public static RoleSummaryDto ToSummaryDto(Role role) =>
        new(role.Id, role.Code, role.Name, role.Level, role.IsActive);

    public static RoleDto ToDto(Role role) =>
        new(
            role.Id,
            role.Code,
            role.Name,
            role.Level,
            role.IsActive,
            role.RolePermissions
                .Select(rolePermission => ToDto(rolePermission.Permission))
                .OrderBy(permission => permission.Code, StringComparer.Ordinal)
                .ToArray());

    public static PermissionDto ToDto(Permission permission) =>
        new(permission.Id, permission.Code, permission.Name, permission.Description, permission.Modulo, permission.IsActive);

    public static AuditRecordDto ToDto(AuditRecord auditRecord) =>
        new(
            auditRecord.Id,
            auditRecord.OccurredAtUtc,
            auditRecord.ActorUserId,
            auditRecord.Action,
            auditRecord.EntityType,
            auditRecord.EntityId,
            auditRecord.Details,
            auditRecord.CorrelationId);

    public static PagedResult<TDestination> Map<TSource, TDestination>(
        PagedResult<TSource> source,
        Func<TSource, TDestination> mapper) =>
        new(source.Items.Select(mapper).ToArray(), source.Page, source.PageSize, source.TotalCount);
}
