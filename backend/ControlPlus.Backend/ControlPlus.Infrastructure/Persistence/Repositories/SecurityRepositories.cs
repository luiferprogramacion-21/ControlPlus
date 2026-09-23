using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using ControlPlus.Domain.OfficialModel;
using User = ControlPlus.Domain.OfficialModel.Usuario;
using Role = ControlPlus.Domain.OfficialModel.Rol;
using Permission = ControlPlus.Domain.OfficialModel.Permiso;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfUserRepository(OfficialControlPlusDbContext dbContext) : IUserRepository
{
    private IQueryable<User> DetailedUsers => dbContext.Usuario
        .Include(user => user.UsuarioRolUsuario)
        .ThenInclude(userRole => userRole!.Rol)
        .ThenInclude(role => role.RolPermiso)
        .ThenInclude(rolePermission => rolePermission.Permiso)
        .Include(user => user.UsuarioPermiso)
        .ThenInclude(userPermission => userPermission.Permiso);

    public Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default) =>
        dbContext.Usuario.AnyAsync(cancellationToken);

    public Task<bool> HasAvailableAdministratorAsync(CancellationToken cancellationToken = default) =>
        dbContext.Usuario.AnyAsync(
            user => user.Activo &&
                    user.BloqueoHasta == null &&
                    user.UsuarioRolUsuario != null &&
                    user.UsuarioRolUsuario.Rol.Activo &&
                    user.UsuarioRolUsuario.Rol.Codigo == RoleCodes.Administrator,
            cancellationToken);

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        DetailedUsers.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        var normalizedUserName = NormalizeUserName(userName);
        return DetailedUsers.SingleOrDefaultAsync(
            user => user.NombreUsuarioNormalizado == normalizedUserName,
            cancellationToken);
    }

    public async Task<UserAuthorizationSnapshot?> GetAuthorizationSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await DetailedUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null || !user.Activo || user.BloqueoHasta != null)
        {
            return null;
        }

        var activeRoles = user.UserRoles
            .Select(userRole => userRole.Role)
            .Where(role => role.Activo)
            .OrderByDescending(role => role.Level)
            .ThenBy(role => role.Code, StringComparer.Ordinal)
            .ToArray();

        var roles = activeRoles
            .Select(role => new RoleClaim(role.Id, role.Code, role.Name, role.Level))
            .ToArray();

        var permissionsById = activeRoles
            .SelectMany(role => role.RolePermissions)
            .Select(rolePermission => rolePermission.Permission)
            .Where(permission => permission.Activo)
            .GroupBy(permission => permission.Id)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var userPermission in user.UsuarioPermiso.Where(x => x.Permiso.Activo))
        {
            if (userPermission.IsRevoked)
            {
                permissionsById.Remove(userPermission.PermisoId);
            }
            else if (userPermission.IsGranted)
            {
                permissionsById[userPermission.PermisoId] = userPermission.Permiso;
            }
        }

        var permissions = permissionsById.Values
            .OrderBy(permission => permission.Code, StringComparer.Ordinal)
            .Select(permission => new PermissionClaim(permission.Id, permission.Code, permission.Name))
            .ToArray();

        var highestRoleLevel = roles.Length == 0
            ? RoleLevel.None
            : roles.Max(role => role.Level);

        return new UserAuthorizationSnapshot(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.SecurityStamp,
            highestRoleLevel,
            roles,
            permissions);
    }

    public async Task<IReadOnlyCollection<User>> GetByRoleIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        await DetailedUsers
            .Where(user => user.UsuarioRolUsuario != null && user.UsuarioRolUsuario.RolId == roleId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<User>> GetByPermissionOverrideIdAsync(
        Guid permissionId,
        CancellationToken cancellationToken = default) =>
        await DetailedUsers
            .Where(user => user.UsuarioPermiso.Any(userPermission => userPermission.PermisoId == permissionId))
            .ToArrayAsync(cancellationToken);

    public async Task<PagedResult<User>> ListAsync(
        UserListQuery query,
        CancellationToken cancellationToken = default)
    {
        var users = DetailedUsers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            users = users.Where(user =>
                EF.Functions.ILike(user.NombreUsuario, pattern) ||
                EF.Functions.ILike(user.NombreCompleto, pattern));
        }

        if (query.IsActive is bool isActive)
        {
            users = isActive
                ? users.Where(user => user.Activo && user.BloqueoHasta == null)
                : users.Where(user => !user.Activo || user.BloqueoHasta != null);
        }

        if (query.IsLocked is bool isLocked)
        {
            users = isLocked
                ? users.Where(user => user.BloqueoHasta != null)
                : users.Where(user => user.BloqueoHasta == null);
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(user => user.NombreUsuario)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return new PagedResult<User>(items, query.Page, query.PageSize, totalCount);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        dbContext.Usuario.AddAsync(user, cancellationToken).AsTask();

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task SetPermissionOverrideAsync(
        User user,
        Guid permissionId,
        bool granted,
        Guid assignedByUserId,
        DateTimeOffset assignedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = user.UsuarioPermiso.SingleOrDefault(x => x.PermisoId == permissionId);
        if (existing is not null)
        {
            existing.Efecto = granted ? UserPermissionEffects.Grant : UserPermissionEffects.Revoke;
            existing.FechaAsignacion = assignedAtUtc.UtcDateTime;
            existing.AsignadoPorId = assignedByUserId;
            return;
        }

        var permissionOverride = new UsuarioPermiso
        {
            UsuarioId = user.Id,
            PermisoId = permissionId,
            Efecto = granted ? UserPermissionEffects.Grant : UserPermissionEffects.Revoke,
            FechaAsignacion = assignedAtUtc.UtcDateTime,
            AsignadoPorId = assignedByUserId,
            Usuario = user
        };
        user.UsuarioPermiso.Add(permissionOverride);
        await dbContext.UsuarioPermiso.AddAsync(permissionOverride, cancellationToken);
    }

    public Task ClearPermissionOverridesAsync(User user, CancellationToken cancellationToken = default)
    {
        dbContext.UsuarioPermiso.RemoveRange(user.UsuarioPermiso);
        user.UsuarioPermiso.Clear();
        return Task.CompletedTask;
    }

    public async Task ReplaceRoleAsync(
        User user,
        Role role,
        Guid assignedByUserId,
        DateTimeOffset assignedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (user.UsuarioRolUsuario is { } currentRole)
        {
            dbContext.UsuarioRol.Remove(currentRole);
        }

        user.ReplaceRole(role, assignedAtUtc, assignedByUserId);
        await dbContext.UsuarioRol.AddAsync(user.UsuarioRolUsuario!, cancellationToken);
    }

    private static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();
}

public sealed class EfRoleRepository(OfficialControlPlusDbContext dbContext) : IRoleRepository
{
    private IQueryable<Role> DetailedRoles => dbContext.Rol
        .Include(role => role.RolPermiso)
        .ThenInclude(rolePermission => rolePermission.Permiso);

    public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        DetailedRoles.SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    public Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        return DetailedRoles.SingleOrDefaultAsync(role => role.Codigo == normalizedCode, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Role>> GetByIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
        {
            return Array.Empty<Role>();
        }

        return await DetailedRoles
            .Where(role => roleIds.Contains(role.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Role>> GetByPermissionIdAsync(
        Guid permissionId,
        CancellationToken cancellationToken = default) =>
        await DetailedRoles
            .Where(role => role.RolPermiso.Any(rolePermission => rolePermission.PermisoId == permissionId))
            .ToArrayAsync(cancellationToken);

    public async Task<PagedResult<Role>> ListAsync(
        RoleListQuery query,
        CancellationToken cancellationToken = default)
    {
        var roles = DetailedRoles.AsQueryable();
        if (query.IsActive is bool isActive)
        {
            roles = roles.Where(role => role.Activo == isActive);
        }

        var totalCount = await roles.CountAsync(cancellationToken);
        var items = await roles
            .OrderBy(role => role.Codigo)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return new PagedResult<Role>(items, query.Page, query.PageSize, totalCount);
    }

    public Task AddAsync(Role role, CancellationToken cancellationToken = default) =>
        dbContext.Rol.AddAsync(role, cancellationToken).AsTask();

    public Task UpdateAsync(Role role, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class EfPermissionRepository(OfficialControlPlusDbContext dbContext) : IPermissionRepository
{
    public Task<Permission?> GetByIdAsync(Guid permissionId, CancellationToken cancellationToken = default) =>
        dbContext.Permiso.SingleOrDefaultAsync(permission => permission.Id == permissionId, cancellationToken);

    public Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        return dbContext.Permiso.SingleOrDefaultAsync(permission => permission.Codigo == normalizedCode, cancellationToken);
    }

    public async Task<PagedResult<Permission>> ListAsync(
        PermissionListQuery query,
        CancellationToken cancellationToken = default)
    {
        var permissions = dbContext.Permiso.AsQueryable();
        if (query.IsActive is bool isActive)
        {
            permissions = permissions.Where(permission => permission.Activo == isActive);
        }

        var totalCount = await permissions.CountAsync(cancellationToken);
        var items = await permissions
            .OrderBy(permission => permission.Codigo)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return new PagedResult<Permission>(items, query.Page, query.PageSize, totalCount);
    }

    public Task AddAsync(Permission permission, CancellationToken cancellationToken = default) =>
        dbContext.Permiso.AddAsync(permission, cancellationToken).AsTask();

    public Task UpdateAsync(Permission permission, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class EfAuditRepository(OfficialControlPlusDbContext dbContext) : IAuditRepository
{
    public async Task AddAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default)
    {
        var installationId = await dbContext.Instalacion
            .Where(installation => installation.Activo)
            .OrderBy(installation => installation.FechaInstalacion)
            .Select(installation => installation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (installationId == Guid.Empty)
        {
            throw new InvalidOperationException("An active installation is required before writing audit events.");
        }

        var correlationId = Guid.TryParse(auditRecord.CorrelationId, out var parsedCorrelationId)
            ? parsedCorrelationId
            : Guid.CreateVersion7();

        await dbContext.EventoAuditoria.AddAsync(new EventoAuditoria
        {
            Id = auditRecord.Id,
            UsuarioId = auditRecord.ActorUserId,
            InstalacionId = installationId,
            FechaHora = auditRecord.OccurredAtUtc.UtcDateTime,
            Accion = auditRecord.Action.ToString().ToUpperInvariant(),
            Entidad = auditRecord.EntityType,
            EntidadId = auditRecord.EntityId,
            Resultado = auditRecord.Result,
            UsuarioAutorizadorId = auditRecord.AuthorizerUserId,
            SesionOperadorId = auditRecord.OperatorSessionId,
            TerminalId = auditRecord.TerminalId,
            Motivo = auditRecord.Reason,
            DatosNuevos = auditRecord.Details,
            CorrelacionId = correlationId
        }, cancellationToken);
    }

    public async Task<PagedResult<AuditRecord>> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var records = dbContext.EventoAuditoria.AsNoTracking().AsQueryable();

        if (query.ActorUserId is Guid actorUserId)
        {
            records = records.Where(record => record.UsuarioId == actorUserId);
        }

        if (query.Action is { } action)
        {
            var actionCode = action.ToString().ToUpperInvariant();
            records = records.Where(record => record.Accion == actionCode);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            records = records.Where(record => record.Entidad == entityType);
        }

        if (query.EntityId is Guid entityId)
        {
            records = records.Where(record => record.EntidadId == entityId);
        }

        if (query.FromUtc is DateTimeOffset fromUtc)
        {
            records = records.Where(record => record.FechaHora >= fromUtc.UtcDateTime);
        }

        if (query.ToUtc is DateTimeOffset toUtc)
        {
            records = records.Where(record => record.FechaHora <= toUtc.UtcDateTime);
        }

        var totalCount = await records.CountAsync(cancellationToken);
        var persistedItems = await records
            .OrderByDescending(record => record.FechaHora)
            .ThenByDescending(record => record.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        var items = persistedItems
            .Select(record => AuditRecord.Restore(
                record.Id,
                record.UsuarioId,
                Enum.TryParse<AuditAction>(record.Accion, true, out var action) ? action : AuditAction.Unknown,
                record.Entidad ?? "AUDITORIA",
                record.EntidadId,
                record.DatosNuevos,
                new DateTimeOffset(DateTime.SpecifyKind(record.FechaHora, DateTimeKind.Utc)),
                record.CorrelacionId.ToString(),
                record.UsuarioAutorizadorId,
                record.SesionOperadorId,
                record.TerminalId,
                record.Motivo,
                record.Resultado))
            .ToArray();

        return new PagedResult<AuditRecord>(items, query.Page, query.PageSize, totalCount);
    }
}

public sealed class EfUnitOfWork(OfficialControlPlusDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
