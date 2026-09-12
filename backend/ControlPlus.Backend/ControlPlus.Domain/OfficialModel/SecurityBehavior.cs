using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlPlus.Domain.OfficialModel;

public partial class Usuario
{
    public const int MaximumFailedLoginAttempts = 5;

    public string UserName => NombreUsuario;
    public string NormalizedUserName => NombreUsuarioNormalizado;
    public string DisplayName => NombreCompleto;
    public string SecurityStamp => SelloSeguridad;
    public int FailedLoginAttempts => IntentosFallidos;
    public DateTimeOffset? LockedAtUtc => BloqueoHasta is null ? null : new DateTimeOffset(DateTime.SpecifyKind(BloqueoHasta.Value, DateTimeKind.Utc));
    public DateTimeOffset? LastLoginAtUtc => UltimoAcceso is null ? null : new DateTimeOffset(DateTime.SpecifyKind(UltimoAcceso.Value, DateTimeKind.Utc));
    public bool IsLocked => BloqueoHasta is not null;
    public bool IsActive => Activo && !IsLocked;
    public UserStatus Status => !Activo ? UserStatus.Inactive : IsLocked ? UserStatus.Locked : UserStatus.Active;
    [NotMapped]
    public IReadOnlyCollection<UsuarioRol> UserRoles => UsuarioRolUsuario is null ? [] : [UsuarioRolUsuario];

    public RoleLevel HighestRoleLevel => UsuarioRolUsuario?.Rol is { Activo: true } role
        ? role.Level
        : RoleLevel.None;

    public static Usuario Create(string userName, string displayName, string passwordHash, DateTimeOffset createdAtUtc)
    {
        var normalizedName = DomainGuard.RequiredText(userName, nameof(userName));
        return new Usuario
        {
            Id = Guid.CreateVersion7(),
            NombreUsuario = normalizedName,
            NombreUsuarioNormalizado = normalizedName.ToUpperInvariant(),
            NombreCompleto = DomainGuard.RequiredText(displayName, nameof(displayName)),
            PasswordHash = DomainGuard.RequiredText(passwordHash, nameof(passwordHash)),
            SelloSeguridad = NewStamp(),
            SelloConcurrencia = NewStamp(),
            Activo = true,
            FechaCreacion = UtcDateTime(createdAtUtc),
            Version = 1
        };
    }

    public bool RegisterFailedLogin(DateTimeOffset occurredAtUtc)
    {
        if (!IsActive) return false;
        IntentosFallidos++;
        FechaModificacion = UtcDateTime(occurredAtUtc);
        Version++;
        if (IntentosFallidos < MaximumFailedLoginAttempts) return false;
        BloqueoHasta = UtcDateTime(occurredAtUtc);
        RotateSecurityStamp();
        return true;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset occurredAtUtc)
    {
        if (!IsActive) throw new DomainRuleViolationException("Only active users can sign in.");
        IntentosFallidos = 0;
        UltimoAcceso = UtcDateTime(occurredAtUtc);
        FechaModificacion = UtcDateTime(occurredAtUtc);
        Version++;
    }

    public void ChangePasswordHash(string passwordHash, DateTimeOffset changedAtUtc)
    {
        PasswordHash = DomainGuard.RequiredText(passwordHash, nameof(passwordHash));
        Touch(changedAtUtc);
        RotateSecurityStamp();
    }

    public void UpdateDisplayName(string displayName, DateTimeOffset updatedAtUtc)
    {
        NombreCompleto = DomainGuard.RequiredText(displayName, nameof(displayName));
        Touch(updatedAtUtc);
    }

    public void Activate(DateTimeOffset changedAtUtc)
    {
        if (IsLocked) throw new DomainRuleViolationException("A locked user must be reactivated by a higher role.");
        Activo = true;
        IntentosFallidos = 0;
        Touch(changedAtUtc);
        RotateSecurityStamp();
    }

    public void Deactivate(DateTimeOffset changedAtUtc)
    {
        if (IsLocked) throw new DomainRuleViolationException("A locked user must be reactivated before its status can change.");
        Activo = false;
        Touch(changedAtUtc);
        RotateSecurityStamp();
    }

    public bool CanBeReactivatedBy(RoleLevel actingRoleLevel) =>
        IsLocked && RoleHierarchy.IsDefined(actingRoleLevel) && RoleHierarchy.IsHigherThan(actingRoleLevel, HighestRoleLevel);

    public void Reactivate(RoleLevel actingRoleLevel, DateTimeOffset changedAtUtc)
    {
        if (!IsLocked) throw new DomainRuleViolationException("Only locked users can be reactivated.");
        if (!CanBeReactivatedBy(actingRoleLevel)) throw new DomainRuleViolationException("A strictly higher role is required to reactivate this user.");
        Activo = true;
        IntentosFallidos = 0;
        BloqueoHasta = null;
        Touch(changedAtUtc);
        RotateSecurityStamp();
    }

    public bool AddRole(Rol role, DateTimeOffset assignedAtUtc, Guid? assignedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!role.IsActive) throw new DomainRuleViolationException("An inactive role cannot be assigned.");
        if (UsuarioRolUsuario?.RolId == role.Id) return false;
        if (UsuarioRolUsuario is not null) throw new DomainRuleViolationException("The official model allows one primary role per user.");
        UsuarioRolUsuario = new UsuarioRol
        {
            UsuarioId = Id,
            RolId = role.Id,
            AsignadoPorId = assignedByUserId,
            FechaAsignacion = UtcDateTime(assignedAtUtc),
            Usuario = this,
            Rol = role
        };
        Touch(assignedAtUtc);
        RotateSecurityStamp();
        return true;
    }

    public bool RemoveRole(Guid roleId, DateTimeOffset changedAtUtc)
    {
        if (UsuarioRolUsuario?.RolId != roleId) return false;
        UsuarioRolUsuario = null;
        Touch(changedAtUtc);
        RotateSecurityStamp();
        return true;
    }

    public string RotateSecurityStamp()
    {
        SelloSeguridad = NewStamp();
        SelloConcurrencia = NewStamp();
        return SelloSeguridad;
    }

    private void Touch(DateTimeOffset at)
    {
        FechaModificacion = UtcDateTime(at);
        Version++;
    }

    private static DateTime UtcDateTime(DateTimeOffset value) => value.UtcDateTime;
    private static string NewStamp() => Guid.CreateVersion7().ToString("N");
}

public partial class Rol
{
    public string Code => Codigo;
    public string Name => Nombre;
    public bool IsActive => Activo;
    public DateTimeOffset CreatedAtUtc => new(DateTime.SpecifyKind(FechaCreacion, DateTimeKind.Utc));
    public DateTimeOffset? UpdatedAtUtc => FechaModificacion is null ? null : new DateTimeOffset(DateTime.SpecifyKind(FechaModificacion.Value, DateTimeKind.Utc));
    [NotMapped]
    public IReadOnlyCollection<RolPermiso> RolePermissions => RolPermiso.ToArray();
    public RoleLevel Level => Codigo switch
    {
        "CAJERO" => RoleLevel.Cajero,
        "SUPERVISOR" => RoleLevel.Supervisor,
        "ADMINISTRADOR" => RoleLevel.Administrador,
        _ => RoleLevel.None
    };

    public static Rol Create(string code, string name, RoleLevel level, DateTimeOffset createdAtUtc)
    {
        var normalizedCode = DomainGuard.NormalizeCode(code, nameof(code));
        var role = new Rol
        {
            Id = Guid.CreateVersion7(), Codigo = normalizedCode, Nombre = DomainGuard.RequiredText(name, nameof(name)),
            EsPredefinido = normalizedCode is "CAJERO" or "SUPERVISOR" or "ADMINISTRADOR",
            PermisosEditables = true, Activo = true, FechaCreacion = createdAtUtc.UtcDateTime, Version = 1
        };
        if (role.Level != level) throw new DomainRuleViolationException("Role authority is defined by the approved role code.");
        return role;
    }

    public void Update(string name, RoleLevel level, DateTimeOffset updatedAtUtc)
    {
        if (Level != level) throw new DomainRuleViolationException("Role authority is defined by the approved role code.");
        Nombre = DomainGuard.RequiredText(name, nameof(name)); Touch(updatedAtUtc);
    }
    public void Activate(DateTimeOffset at) { Activo = true; Touch(at); }
    public void Deactivate(DateTimeOffset at) { Activo = false; Touch(at); }
    public bool HasAtLeastLevel(RoleLevel required) => IsActive && RoleHierarchy.HasAtLeast(Level, required);
    public bool IsHigherThan(RoleLevel target) => IsActive && RoleHierarchy.IsHigherThan(Level, target);
    public bool HasPermission(string code) => IsActive && RolPermiso.Any(x => x.Permiso.Activo && x.Permiso.Codigo == DomainGuard.NormalizeCode(code, nameof(code)));
    public bool GrantPermission(Permiso permission, DateTimeOffset at, Guid? grantedByUserId = null)
    {
        if (!permission.Activo) throw new DomainRuleViolationException("An inactive permission cannot be granted.");
        if (RolPermiso.Any(x => x.PermisoId == permission.Id)) return false;
        RolPermiso.Add(new RolPermiso { RolId = Id, PermisoId = permission.Id, FechaAsignacion = at.UtcDateTime, Rol = this, Permiso = permission });
        Touch(at); return true;
    }
    public bool RevokePermission(Guid permissionId, DateTimeOffset at)
    {
        var link = RolPermiso.SingleOrDefault(x => x.PermisoId == permissionId); if (link is null) return false;
        RolPermiso.Remove(link); Touch(at); return true;
    }
    private void Touch(DateTimeOffset at) { FechaModificacion = at.UtcDateTime; Version++; }
}

public partial class Permiso
{
    public string Code => Codigo;
    public string Name => Nombre;
    public string? Description => Descripcion;
    public bool IsActive => Activo;
    public static Permiso Create(string code, string name, string? description, string module, DateTimeOffset createdAtUtc) => new()
    {
        Id = Guid.CreateVersion7(), Codigo = DomainGuard.NormalizeCode(code, nameof(code)),
        Nombre = DomainGuard.RequiredText(name, nameof(name)), Descripcion = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        Modulo = DomainGuard.NormalizeCode(module, nameof(module)), Activo = true, FechaCreacion = createdAtUtc.UtcDateTime
    };
    public void Update(string name, string? description, DateTimeOffset at) { Nombre = DomainGuard.RequiredText(name, nameof(name)); Descripcion = string.IsNullOrWhiteSpace(description) ? null : description.Trim(); }
    public void Activate(DateTimeOffset at) => Activo = true;
    public void Deactivate(DateTimeOffset at) => Activo = false;
}

public partial class UsuarioRol
{
    public Guid UserId => UsuarioId;
    public Guid RoleId => RolId;
    public Guid? AssignedByUserId => AsignadoPorId;
    public DateTimeOffset AssignedAtUtc => new(DateTime.SpecifyKind(FechaAsignacion, DateTimeKind.Utc));
    [NotMapped] public Usuario User => Usuario;
    [NotMapped] public Rol Role => Rol;
}

public partial class RolPermiso
{
    public Guid RoleId => RolId;
    public Guid PermissionId => PermisoId;
    public DateTimeOffset GrantedAtUtc => new(DateTime.SpecifyKind(FechaAsignacion, DateTimeKind.Utc));
    [NotMapped] public Rol Role => Rol;
    [NotMapped] public Permiso Permission => Permiso;
}
