using ControlPlus.Application.Common;

namespace ControlPlus.Application.Security.Services;

internal static class SecurityErrors
{
    public static readonly ApplicationError InvalidCredentials =
        new("authentication.invalid_credentials", "Usuario o contraseña inválidos.");

    public static readonly ApplicationError UserInactive =
        new("user.inactive", "El usuario está inactivo.");

    public static readonly ApplicationError UserLocked =
        new("user.locked", "El usuario está bloqueado.");

    public static readonly ApplicationError InitialAdministratorAlreadyConfigured =
        new("authentication.bootstrap_completed", "El administrador inicial ya fue configurado.");

    public static readonly ApplicationError AdministratorRoleMissing =
        new("security.bootstrap_role_missing", "No se encontró el rol Administrador requerido para la configuración inicial.");

    public static readonly ApplicationError AdministratorRecoveryUnavailable =
        ApplicationError.Conflict("La recuperación no está disponible mientras exista un Administrador activo y no bloqueado.");

    public static readonly ApplicationError InitialAdministratorRecoveryTargetInvalid =
        ApplicationError.Conflict("El usuario indicado no es el Administrador inicial recuperable.");

    public static readonly ApplicationError UserNameAlreadyExists =
        new("user.user_name_already_exists", "El nombre de usuario ya está en uso.");

    public static readonly ApplicationError RoleCodeAlreadyExists =
        new("role.code_already_exists", "El código del rol ya está en uso.");

    public static readonly ApplicationError PermissionCodeAlreadyExists =
        new("permission.code_already_exists", "El código del permiso ya está en uso.");

    public static readonly ApplicationError AtLeastOneRoleRequired =
        new("user.role_required", "El usuario debe tener al menos un rol.");

    public static readonly ApplicationError ExactlyOneRoleRequired =
        new("user.single_role_required", "El modelo oficial permite exactamente un rol primario por usuario.");

    public static readonly ApplicationError RoleAlreadyAssigned =
        new("user.role_already_assigned", "El rol ya está asignado al usuario.");

    public static readonly ApplicationError RoleNotAssigned =
        new("user.role_not_assigned", "El rol no está asignado al usuario.");

    public static readonly ApplicationError PermissionAlreadyGranted =
        new("role.permission_already_granted", "El permiso ya está asignado al rol.");

    public static readonly ApplicationError PermissionNotGranted =
        new("role.permission_not_granted", "El permiso no está asignado al rol.");

    public static readonly ApplicationError RoleInactive =
        new("role.inactive", "El rol está inactivo.");

    public static readonly ApplicationError PermissionInactive =
        new("permission.inactive", "El permiso está inactivo.");

    public static readonly ApplicationError IncorrectCurrentPassword =
        new("user.current_password_invalid", "La contraseña actual no es válida.");

    public static readonly ApplicationError CannotManageSameOrHigherRole =
        new("authorization.insufficient_role_level", "No puede administrar un rol del mismo o mayor nivel.");
}
