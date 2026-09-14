using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using User = ControlPlus.Domain.OfficialModel.Usuario;
using Role = ControlPlus.Domain.OfficialModel.Rol;

namespace ControlPlus.Application.Security.Services;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UserManagementService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IPasswordHasher passwordHasher,
        IPermissionChecker permissionChecker,
        IAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _passwordHasher = passwordHasher;
        _permissionChecker = permissionChecker;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<UserDto>> CreateAsync(
        ActorContext actor,
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var userName = SecurityInput.RequiredText(request.UserName, "nombre de usuario");
        if (userName.IsFailure)
        {
            return Result.Failure<UserDto>(userName.Error!);
        }

        var displayName = SecurityInput.RequiredText(request.DisplayName, "nombre visible");
        if (displayName.IsFailure)
        {
            return Result.Failure<UserDto>(displayName.Error!);
        }

        var password = SecurityInput.RequiredPassword(request.Password);
        if (password.IsFailure)
        {
            return Result.Failure<UserDto>(password.Error!);
        }

        var requestedRoleIds = request.RoleIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? Array.Empty<Guid>();
        if (requestedRoleIds.Length != 1)
        {
            return Result.Failure<UserDto>(SecurityErrors.ExactlyOneRoleRequired);
        }

        if (await _userRepository.GetByUserNameAsync(userName.Value!, cancellationToken) is not null)
        {
            return Result.Failure<UserDto>(SecurityErrors.UserNameAlreadyExists);
        }

        var roles = await _roleRepository.GetByIdsAsync(requestedRoleIds, cancellationToken);
        if (roles.Count != requestedRoleIds.Length)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("uno o más roles solicitados"));
        }

        if (roles.Any(role => !role.IsActive))
        {
            return Result.Failure<UserDto>(SecurityErrors.RoleInactive);
        }

        if (roles.Any(role => !CanManageRole(actor, role)))
        {
            return Result.Failure<UserDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        var now = _clock.UtcNow;
        var user = User.Create(userName.Value!, displayName.Value!, _passwordHasher.Hash(password.Value!), now);
        var role = roles.Single();
        user.AddRole(role, now, actor.UserId);

        await _userRepository.AddAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.UserCreated,
            nameof(User),
            user.Id,
            new { user.UserName, user.DisplayName, Role = role.Code },
            cancellationToken);

        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.RoleAssigned,
            nameof(User),
            user.Id,
            new { RoleId = role.Id, role.Code },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<UserDto>> UpdateAsync(
        ActorContext actor,
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var displayName = SecurityInput.RequiredText(request.DisplayName, "nombre visible");
        if (displayName.IsFailure)
        {
            return Result.Failure<UserDto>(displayName.Error!);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("el usuario"));
        }

        if (!CanManageUser(actor, user))
        {
            return Result.Failure<UserDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        user.UpdateDisplayName(displayName.Value!, _clock.UtcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.UserUpdated,
            nameof(User),
            user.Id,
            new { user.DisplayName },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<UserDto>> ActivateAsync(
        ActorContext actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("el usuario"));
        }

        if (!CanManageUser(actor, user))
        {
            return Result.Failure<UserDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        if (user.IsLocked)
        {
            return Result.Failure<UserDto>(SecurityErrors.UserLocked);
        }

        user.Activate(_clock.UtcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.UserActivated,
            nameof(User),
            user.Id,
            new { user.UserName },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<UserDto>> DeactivateAsync(
        ActorContext actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("el usuario"));
        }

        if (!CanManageUser(actor, user))
        {
            return Result.Failure<UserDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        if (user.IsLocked)
        {
            return Result.Failure<UserDto>(SecurityErrors.UserLocked);
        }

        if (user.HighestRoleLevel == RoleLevel.Administrador &&
            !await HasAnotherSecurityAdministratorAsync(user.Id, cancellationToken))
        {
            return Result.Failure<UserDto>(SecurityErrors.LastSecurityAdministratorRequired);
        }

        user.Deactivate(_clock.UtcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.UserDeactivated,
            nameof(User),
            user.Id,
            new { user.UserName },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<UserDto>> ReactivateAsync(
        ActorContext actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("el usuario"));
        }

        if (!user.IsLocked)
        {
            return Result.Failure<UserDto>(ApplicationError.Conflict("El usuario no está bloqueado."));
        }

        if (!user.CanBeReactivatedBy(actor.HighestRoleLevel))
        {
            return Result.Failure<UserDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        user.Reactivate(actor.HighestRoleLevel, _clock.UtcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.UserReactivated,
            nameof(User),
            user.Id,
            new { user.UserName },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result> ChangePasswordAsync(
        ActorContext actor,
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var newPassword = SecurityInput.RequiredPassword(request.NewPassword, "nueva contraseña");
        if (newPassword.IsFailure)
        {
            return Result.Failure(newPassword.Error!);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(ApplicationError.NotFound("el usuario"));
        }

        if (actor.UserId == userId)
        {
            var currentPassword = SecurityInput.RequiredPassword(request.CurrentPassword, "contraseña actual");
            if (currentPassword.IsFailure)
            {
                return Result.Failure(currentPassword.Error!);
            }

            if (!_passwordHasher.Verify(currentPassword.Value!, user.PasswordHash))
            {
                return Result.Failure(SecurityErrors.IncorrectCurrentPassword);
            }
        }
        else
        {
            var isSupervisorResettingCashier =
                actor.HighestRoleLevel == RoleLevel.Supervisor &&
                user.HighestRoleLevel == RoleLevel.Cajero &&
                actor.HasPermission(PermissionCodes.CashierPasswordsReset);

            if (!isSupervisorResettingCashier)
            {
                var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
                if (authorizationError is not null)
                {
                    return Result.Failure(authorizationError);
                }

                if (!CanManageUser(actor, user))
                {
                    return Result.Failure(SecurityErrors.CannotManageSameOrHigherRole);
                }
            }
        }

        user.ChangePasswordHash(_passwordHasher.Hash(newPassword.Value!), _clock.UtcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.PasswordChanged,
            nameof(User),
            user.Id,
            new { user.UserName, ChangedBySelf = actor.UserId == user.Id },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<UserDto>> AssignRoleAsync(
        ActorContext actor,
        Guid userId,
        AssignRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var authorizationError = await RequireUsersManageAsync(actor, cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("el usuario"));
        }

        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<UserDto>(ApplicationError.NotFound("el rol"));
        }

        if (!role.IsActive)
        {
            return Result.Failure<UserDto>(SecurityErrors.RoleInactive);
        }

        if (role.Code is not (RoleCodes.Administrator or RoleCodes.Supervisor or RoleCodes.Cashier))
        {
            return Result.Failure<UserDto>(SecurityErrors.FixedRolesOnly);
        }

        if (!CanManageUser(actor, user) || !CanManageRole(actor, role))
        {
            return Result.Failure<UserDto>(SecurityErrors.CannotManageSameOrHigherRole);
        }

        var currentRole = user.UsuarioRolUsuario?.Rol;
        if (currentRole?.Id == role.Id)
        {
            return Result.Failure<UserDto>(SecurityErrors.RoleAlreadyAssigned);
        }

        if (currentRole?.Code == RoleCodes.Administrator && role.Code != RoleCodes.Administrator &&
            !await HasAnotherSecurityAdministratorAsync(user.Id, cancellationToken))
        {
            return Result.Failure<UserDto>(SecurityErrors.LastSecurityAdministratorRequired);
        }

        var now = _clock.UtcNow;
        await _userRepository.ReplaceRoleAsync(user, role, actor.UserId, now, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actor.UserId,
            AuditAction.RoleAssigned,
            nameof(User),
            user.Id,
            new
            {
                PreviousRoleId = currentRole?.Id,
                PreviousRoleCode = currentRole?.Code,
                RoleId = role.Id,
                role.Code
            },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<UserDto>> GetByIdAsync(
        ActorContext actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.UsersRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<UserDto>(authorizationError);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null
            ? Result.Failure<UserDto>(ApplicationError.NotFound("el usuario"))
            : Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<PagedResult<UserDto>>> ListAsync(
        ActorContext actor,
        UserListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.UsersRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PagedResult<UserDto>>(authorizationError);
        }

        var pageValidation = SecurityInput.ValidatePage(query.Page, query.PageSize);
        if (pageValidation.IsFailure)
        {
            return Result.Failure<PagedResult<UserDto>>(pageValidation.Error!);
        }

        var users = await _userRepository.ListAsync(query, cancellationToken);
        return Result.Success(SecurityMappings.Map(users, SecurityMappings.ToDto));
    }

    public async Task<Result<IReadOnlyCollection<EffectiveUserPermissionDto>>> GetEffectivePermissionsAsync(
        ActorContext actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var adminError = RequireAdministrator(actor);
        if (adminError is not null) return Result.Failure<IReadOnlyCollection<EffectiveUserPermissionDto>>(adminError);

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return Result.Failure<IReadOnlyCollection<EffectiveUserPermissionDto>>(ApplicationError.NotFound("el usuario"));

        var catalog = await _permissionRepository.ListAsync(new PermissionListQuery(PageSize: 200), cancellationToken);
        var rolePermissionIds = user.UserRoles
            .Where(x => x.Role.IsActive)
            .SelectMany(x => x.Role.RolePermissions)
            .Where(x => x.Permission.IsActive)
            .Select(x => x.PermissionId)
            .ToHashSet();
        var overrides = user.UsuarioPermiso.ToDictionary(x => x.PermisoId);

        var items = catalog.Items.Select(permission =>
        {
            overrides.TryGetValue(permission.Id, out var permissionOverride);
            var granted = permissionOverride?.IsGranted ?? rolePermissionIds.Contains(permission.Id);
            var source = permissionOverride is null ? "ROL" : "INDIVIDUAL";
            return new EffectiveUserPermissionDto(
                permission.Id, permission.Code, permission.Name, granted, source, permissionOverride?.Efecto);
        }).OrderBy(x => x.Code, StringComparer.Ordinal).ToArray();

        return Result.Success<IReadOnlyCollection<EffectiveUserPermissionDto>>(items);
    }

    public async Task<Result> SetPermissionOverrideAsync(
        ActorContext actor,
        Guid userId,
        Guid permissionId,
        SetUserPermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var adminError = RequireAdministrator(actor);
        if (adminError is not null) return Result.Failure(adminError);

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return Result.Failure(ApplicationError.NotFound("el usuario"));
        var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
        if (permission is null || !permission.IsActive) return Result.Failure(ApplicationError.NotFound("el permiso activo"));

        if (!request.Granted && user.HighestRoleLevel == RoleLevel.Administrador &&
            SecurityAdministratorPermissions.Contains(permission.Code) &&
            !await HasAnotherSecurityAdministratorAsync(user.Id, cancellationToken))
        {
            return Result.Failure(SecurityErrors.LastSecurityAdministratorRequired);
        }

        await _userRepository.SetPermissionOverrideAsync(user, permission.Id, request.Granted, actor.UserId, _clock.UtcNow, cancellationToken);
        user.RotateSecurityStamp();
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository, _clock, actor.UserId,
            request.Granted ? AuditAction.UserPermissionGranted : AuditAction.UserPermissionRevoked,
            nameof(User), user.Id,
            new { PermissionId = permission.Id, permission.Code, Effect = request.Granted ? "CONCEDER" : "REVOCAR" },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetPermissionOverridesAsync(
        ActorContext actor,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var adminError = RequireAdministrator(actor);
        if (adminError is not null) return Result.Failure(adminError);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null) return Result.Failure(ApplicationError.NotFound("el usuario"));

        var removed = user.UsuarioPermiso.Count;
        await _userRepository.ClearPermissionOverridesAsync(user, cancellationToken);
        user.RotateSecurityStamp();
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository, _clock, actor.UserId, AuditAction.UserPermissionsReset,
            nameof(User), user.Id, new { RemovedOverrides = removed }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<ApplicationError?> RequireUsersManageAsync(ActorContext actor, CancellationToken cancellationToken) =>
        await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.UsersManage,
            cancellationToken);

    private static readonly IReadOnlySet<string> SecurityAdministratorPermissions = new HashSet<string>(
        [PermissionCodes.UsersManage, PermissionCodes.RolesManage, PermissionCodes.PermissionsManage, PermissionCodes.UserPermissionsManage],
        StringComparer.OrdinalIgnoreCase);

    private static ApplicationError? RequireAdministrator(ActorContext actor) =>
        actor.HighestRoleLevel == RoleLevel.Administrador ? null : SecurityErrors.AdministratorOnly;

    private async Task<bool> HasAnotherSecurityAdministratorAsync(Guid excludedUserId, CancellationToken cancellationToken)
    {
        var administratorRole = await _roleRepository.GetByCodeAsync(RoleCodes.Administrator, cancellationToken);
        if (administratorRole is null) return false;
        var administrators = await _userRepository.GetByRoleIdAsync(administratorRole.Id, cancellationToken);
        foreach (var candidate in administrators.Where(x => x.Id != excludedUserId && x.IsActive))
        {
            var snapshot = await _userRepository.GetAuthorizationSnapshotAsync(candidate.Id, cancellationToken);
            if (snapshot is not null && SecurityAdministratorPermissions.All(snapshot.PermissionCodes.Contains)) return true;
        }
        return false;
    }

    private static bool CanManageUser(ActorContext actor, User user) =>
        actor.HighestRoleLevel == RoleLevel.Administrador ||
        (RoleHierarchy.IsDefined(actor.HighestRoleLevel) &&
         RoleHierarchy.IsHigherThan(actor.HighestRoleLevel, user.HighestRoleLevel));

    private static bool CanManageRole(ActorContext actor, Role role) =>
        actor.HighestRoleLevel == RoleLevel.Administrador ||
        (RoleHierarchy.IsDefined(actor.HighestRoleLevel) &&
         RoleHierarchy.IsHigherThan(actor.HighestRoleLevel, role.Level));
}
