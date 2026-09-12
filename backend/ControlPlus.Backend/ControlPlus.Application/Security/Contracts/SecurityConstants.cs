namespace ControlPlus.Application.Security.Contracts;

/// <summary>
/// Stable role codes used by bootstrap data and JWT claims.
/// </summary>
public static class RoleCodes
{
    public const string Cashier = "CAJERO";
    public const string Supervisor = "SUPERVISOR";
    public const string Administrator = "ADMINISTRADOR";
}

/// <summary>
/// Stable permission codes used by authorization policies and JWT claims.
/// </summary>
public static class PermissionCodes
{
    // Permission.Code is normalized by Domain to uppercase, so policies and seed data use the same canonical values.
    public const string UsersRead = "USERS.READ";
    public const string UsersManage = "USERS.MANAGE";
    public const string RolesRead = "ROLES.READ";
    public const string RolesManage = "ROLES.MANAGE";
    public const string PermissionsRead = "PERMISSIONS.READ";
    public const string PermissionsManage = "PERMISSIONS.MANAGE";
    public const string AuditRead = "AUDIT.READ";
}
