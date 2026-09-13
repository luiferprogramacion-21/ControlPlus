namespace ControlPlus.Domain.Security.Enums;

/// <summary>
/// Business events that must be traceable in the security module.
/// </summary>
public enum AuditAction
{
    Unknown = 0,
    UserCreated = 1,
    UserUpdated = 2,
    UserActivated = 3,
    UserDeactivated = 4,
    UserLocked = 5,
    UserReactivated = 6,
    PasswordChanged = 7,
    LoginSucceeded = 8,
    LoginFailed = 9,
    RoleCreated = 10,
    RoleUpdated = 11,
    RoleActivated = 12,
    RoleDeactivated = 13,
    PermissionCreated = 14,
    PermissionUpdated = 15,
    PermissionActivated = 16,
    PermissionDeactivated = 17,
    RoleAssigned = 18,
    RoleRemoved = 19,
    PermissionGranted = 20,
    PermissionRevoked = 21,
    InitialAdministratorRecovered = 22,
    UserPermissionGranted = 23,
    UserPermissionRevoked = 24,
    UserPermissionsReset = 25,
    RolePermissionsReset = 26,
    CategoryCreated = 27,
    CategoryUpdated = 28,
    CategoryActivated = 29,
    CategoryDeactivated = 30,
    ProductCreated = 31,
    ProductUpdated = 32,
    ProductActivated = 33,
    ProductDeactivated = 34
}
