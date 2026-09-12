using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using User = ControlPlus.Domain.OfficialModel.Usuario;
using Role = ControlPlus.Domain.OfficialModel.Rol;
using Permission = ControlPlus.Domain.OfficialModel.Permiso;

namespace ControlPlus.Application.Security.Services;

public sealed class RoleManagementService : IRoleManagementService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RoleManagementService(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IUserRepository userRepository,
        IPermissionChecker permissionChecker,
        IAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _userRepository = userRepository;
        _permissionChecker = permissionChecker;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<RoleDto>> CreateRoleAsync(
        ActorContext actor,
        CreateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequireRolesManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var code = SecurityInput.RequiredText(request.Code, "código del rol");
        var name = SecurityInput.RequiredText(request.Name, "nombre del rol");
        if (code.IsFailure)
        {
            return Result.Failure<RoleDto>(code.Error!);
        }

        if (name.IsFailure)
        {
            return Result.Failure<RoleDto>(name.Error!);
        }

        if (!RoleHierarchy.IsDefined(request.Level))
        {
            return Result.Failure<RoleDto>(ApplicationError.Validation("El nivel del rol no es válido."));
        }

        if (!CanManageRoleLevel(actor, request.Level))
        {
            return Result.Failure<RoleDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        var normalizedCode = SecurityInput.NormalizeRoleCode(code.Value!);
        if (await _roleRepository.GetByCodeAsync(normalizedCode, cancellationToken) is not null)
        {
            return Result.Failure<RoleDto>(SecurityErrors.RoleCodeAlreadyExists);
        }

        var role = Role.Create(normalizedCode, name.Value!, request.Level, _clock.UtcNow);
        await _roleRepository.AddAsync(role, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.RoleCreated,
            nameof(Role),
            role.Id,
            new { role.Code, role.Name, role.Level },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<RoleDto>> UpdateRoleAsync(
        ActorContext actor,
        Guid roleId,
        UpdateRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequireRolesManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var name = SecurityInput.RequiredText(request.Name, "nombre del rol");
        if (name.IsFailure)
        {
            return Result.Failure<RoleDto>(name.Error!);
        }

        if (!RoleHierarchy.IsDefined(request.Level))
        {
            return Result.Failure<RoleDto>(ApplicationError.Validation("El nivel del rol no es válido."));
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(ApplicationError.NotFound("el rol"));
        }

        if (!CanManageRole(actor, role) || !CanManageRoleLevel(actor, request.Level))
        {
            return Result.Failure<RoleDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        role.Update(name.Value!, request.Level, _clock.UtcNow);
        await _roleRepository.UpdateAsync(role, cancellationToken);
        await InvalidateUsersForRolesAsync(new[] { role }, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.RoleUpdated,
            nameof(Role),
            role.Id,
            new { role.Name, role.Level },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<RoleDto>> ActivateRoleAsync(
        ActorContext actor,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireRolesManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(ApplicationError.NotFound("el rol"));
        }

        if (!CanManageRole(actor, role))
        {
            return Result.Failure<RoleDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        role.Activate(_clock.UtcNow);
        await _roleRepository.UpdateAsync(role, cancellationToken);
        await InvalidateUsersForRolesAsync(new[] { role }, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.RoleActivated,
            nameof(Role),
            role.Id,
            new { role.Code },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<RoleDto>> DeactivateRoleAsync(
        ActorContext actor,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireRolesManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(ApplicationError.NotFound("el rol"));
        }

        if (!CanManageRole(actor, role))
        {
            return Result.Failure<RoleDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        role.Deactivate(_clock.UtcNow);
        await _roleRepository.UpdateAsync(role, cancellationToken);
        await InvalidateUsersForRolesAsync(new[] { role }, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.RoleDeactivated,
            nameof(Role),
            role.Id,
            new { role.Code },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<RoleDto>> GrantPermissionAsync(
        ActorContext actor,
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireRolesManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(ApplicationError.NotFound("el rol"));
        }

        if (!CanManageRole(actor, role))
        {
            return Result.Failure<RoleDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
        if (permission is null)
        {
            return Result.Failure<RoleDto>(ApplicationError.NotFound("el permiso"));
        }

        if (!permission.IsActive)
        {
            return Result.Failure<RoleDto>(SecurityErrors.PermissionInactive);
        }

        if (!role.GrantPermission(permission, _clock.UtcNow, actor.UserId))
        {
            return Result.Failure<RoleDto>(SecurityErrors.PermissionAlreadyGranted);
        }

        await _roleRepository.UpdateAsync(role, cancellationToken);
        await InvalidateUsersForRolesAsync(new[] { role }, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PermissionGranted,
            nameof(Role),
            role.Id,
            new { PermissionId = permission.Id, permission.Code },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<RoleDto>> RevokePermissionAsync(
        ActorContext actor,
        Guid roleId,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireRolesManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(ApplicationError.NotFound("el rol"));
        }

        if (!CanManageRole(actor, role))
        {
            return Result.Failure<RoleDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        var rolePermission = role.RolePermissions.SingleOrDefault(candidate => candidate.PermissionId == permissionId);
        if (rolePermission is null)
        {
            return Result.Failure<RoleDto>(SecurityErrors.PermissionNotGranted);
        }

        role.RevokePermission(permissionId, _clock.UtcNow);
        await _roleRepository.UpdateAsync(role, cancellationToken);
        await InvalidateUsersForRolesAsync(new[] { role }, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PermissionRevoked,
            nameof(Role),
            role.Id,
            new { PermissionId = rolePermission.PermissionId, rolePermission.Permission.Code },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<PermissionDto>> CreatePermissionAsync(
        ActorContext actor,
        CreatePermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequirePermissionsManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PermissionDto>(authorizationError);
        }

        var code = SecurityInput.RequiredText(request.Code, "código del permiso");
        var name = SecurityInput.RequiredText(request.Name, "nombre del permiso");
        if (code.IsFailure)
        {
            return Result.Failure<PermissionDto>(code.Error!);
        }

        if (name.IsFailure)
        {
            return Result.Failure<PermissionDto>(name.Error!);
        }

        var normalizedCode = SecurityInput.NormalizePermissionCode(code.Value!);
        if (await _permissionRepository.GetByCodeAsync(normalizedCode, cancellationToken) is not null)
        {
            return Result.Failure<PermissionDto>(SecurityErrors.PermissionCodeAlreadyExists);
        }

        var module = SecurityInput.RequiredText(request.Module, "módulo");
        if (module.IsFailure)
        {
            return Result.Failure<PermissionDto>(module.Error!);
        }

        var permission = Permission.Create(normalizedCode, name.Value!, request.Description, module.Value!, _clock.UtcNow);
        await _permissionRepository.AddAsync(permission, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PermissionCreated,
            nameof(Permission),
            permission.Id,
            new { permission.Code, permission.Name },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(permission));
    }

    public async Task<Result<PermissionDto>> UpdatePermissionAsync(
        ActorContext actor,
        Guid permissionId,
        UpdatePermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequirePermissionsManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PermissionDto>(authorizationError);
        }

        var name = SecurityInput.RequiredText(request.Name, "nombre del permiso");
        if (name.IsFailure)
        {
            return Result.Failure<PermissionDto>(name.Error!);
        }

        var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
        if (permission is null)
        {
            return Result.Failure<PermissionDto>(ApplicationError.NotFound("el permiso"));
        }

        permission.Update(name.Value!, request.Description, _clock.UtcNow);
        await _permissionRepository.UpdateAsync(permission, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PermissionUpdated,
            nameof(Permission),
            permission.Id,
            new { permission.Name },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(permission));
    }

    public async Task<Result<PermissionDto>> ActivatePermissionAsync(
        ActorContext actor,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequirePermissionsManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PermissionDto>(authorizationError);
        }

        var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
        if (permission is null)
        {
            return Result.Failure<PermissionDto>(ApplicationError.NotFound("el permiso"));
        }

        permission.Activate(_clock.UtcNow);
        await _permissionRepository.UpdateAsync(permission, cancellationToken);
        await InvalidateUsersForPermissionAsync(permission.Id, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PermissionActivated,
            nameof(Permission),
            permission.Id,
            new { permission.Code },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(permission));
    }

    public async Task<Result<PermissionDto>> DeactivatePermissionAsync(
        ActorContext actor,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequirePermissionsManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PermissionDto>(authorizationError);
        }

        var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
        if (permission is null)
        {
            return Result.Failure<PermissionDto>(ApplicationError.NotFound("el permiso"));
        }

        permission.Deactivate(_clock.UtcNow);
        await _permissionRepository.UpdateAsync(permission, cancellationToken);
        await InvalidateUsersForPermissionAsync(permission.Id, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PermissionDeactivated,
            nameof(Permission),
            permission.Id,
            new { permission.Code },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(permission));
    }

    public async Task<Result<RoleDto>> GetRoleByIdAsync(
        ActorContext actor,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.RolesRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<RoleDto>(authorizationError);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        return role is null
            ? Result.Failure<RoleDto>(ApplicationError.NotFound("el rol"))
            : Result.Success(SecurityMappings.ToDto(role));
    }

    public async Task<Result<PagedResult<RoleDto>>> ListRolesAsync(
        ActorContext actor,
        RoleListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.RolesRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PagedResult<RoleDto>>(authorizationError);
        }

        var pageValidation = SecurityInput.ValidatePage(query.Page, query.PageSize);
        if (pageValidation.IsFailure)
        {
            return Result.Failure<PagedResult<RoleDto>>(pageValidation.Error!);
        }

        var roles = await _roleRepository.ListAsync(query, cancellationToken);
        return Result.Success(SecurityMappings.Map(roles, SecurityMappings.ToDto));
    }

    public async Task<Result<PermissionDto>> GetPermissionByIdAsync(
        ActorContext actor,
        Guid permissionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.PermissionsRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PermissionDto>(authorizationError);
        }

        var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
        return permission is null
            ? Result.Failure<PermissionDto>(ApplicationError.NotFound("el permiso"))
            : Result.Success(SecurityMappings.ToDto(permission));
    }

    public async Task<Result<PagedResult<PermissionDto>>> ListPermissionsAsync(
        ActorContext actor,
        PermissionListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.PermissionsRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PagedResult<PermissionDto>>(authorizationError);
        }

        var pageValidation = SecurityInput.ValidatePage(query.Page, query.PageSize);
        if (pageValidation.IsFailure)
        {
            return Result.Failure<PagedResult<PermissionDto>>(pageValidation.Error!);
        }

        var permissions = await _permissionRepository.ListAsync(query, cancellationToken);
        return Result.Success(SecurityMappings.Map(permissions, SecurityMappings.ToDto));
    }

    private async Task<ApplicationError?> RequireRolesManageAsync(ActorContext actor, CancellationToken cancellationToken) =>
        await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.RolesManage,
            cancellationToken);

    private async Task<ApplicationError?> RequirePermissionsManageAsync(ActorContext actor, CancellationToken cancellationToken) =>
        await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.PermissionsManage,
            cancellationToken);

    private async Task InvalidateUsersForPermissionAsync(Guid permissionId, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.GetByPermissionIdAsync(permissionId, cancellationToken);
        await InvalidateUsersForRolesAsync(roles, cancellationToken);
    }

    private async Task InvalidateUsersForRolesAsync(
        IEnumerable<Role> roles,
        CancellationToken cancellationToken)
    {
        var usersById = new Dictionary<Guid, User>();
        foreach (var role in roles.DistinctBy(role => role.Id))
        {
            var users = await _userRepository.GetByRoleIdAsync(role.Id, cancellationToken);
            foreach (var user in users)
            {
                usersById[user.Id] = user;
            }
        }

        foreach (var user in usersById.Values)
        {
            user.RotateSecurityStamp();
            await _userRepository.UpdateAsync(user, cancellationToken);
        }
    }

    private static bool CanManageRole(ActorContext actor, Role role) =>
        CanManageRoleLevel(actor, role.Level);

    private static bool CanManageRoleLevel(ActorContext actor, RoleLevel roleLevel) =>
        actor.HighestRoleLevel == RoleLevel.Administrador ||
        (RoleHierarchy.IsDefined(actor.HighestRoleLevel) &&
         RoleHierarchy.IsHigherThan(actor.HighestRoleLevel, roleLevel));
}
