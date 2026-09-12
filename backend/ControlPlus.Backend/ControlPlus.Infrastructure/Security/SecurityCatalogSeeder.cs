using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Role = ControlPlus.Domain.OfficialModel.Rol;
using Permission = ControlPlus.Domain.OfficialModel.Permiso;

namespace ControlPlus.Infrastructure.Security;

/// <summary>
/// Idempotently prepares the base security catalog for a fresh database.
/// It only adds missing records and grants; it never removes an administrator's later changes.
/// </summary>
public sealed class SecurityCatalogSeeder(
    IPermissionRepository permissionRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly PermissionDefinition[] PermissionDefinitions =
    [
        new(PermissionCodes.UsersRead, "Consultar usuarios", "Permite consultar usuarios y sus roles.", "SEGURIDAD"),
        new(PermissionCodes.UsersManage, "Gestionar usuarios", "Permite crear, modificar, activar, desactivar y reactivar usuarios.", "SEGURIDAD"),
        new(PermissionCodes.RolesRead, "Consultar roles", "Permite consultar roles y sus permisos.", "SEGURIDAD"),
        new(PermissionCodes.RolesManage, "Gestionar roles", "Permite crear, modificar y asignar permisos a roles.", "SEGURIDAD"),
        new(PermissionCodes.PermissionsRead, "Consultar permisos", "Permite consultar el catálogo de permisos.", "SEGURIDAD"),
        new(PermissionCodes.PermissionsManage, "Gestionar permisos", "Permite crear, modificar y cambiar el estado de permisos.", "SEGURIDAD"),
        new(PermissionCodes.AuditRead, "Consultar auditoría", "Permite consultar los eventos de auditoría de seguridad.", "SEGURIDAD")
    ];

    private static readonly RoleDefinition[] RoleDefinitions =
    [
        new(RoleCodes.Cashier, "Cajero", RoleLevel.Cajero, []),
        new(RoleCodes.Supervisor, "Supervisor", RoleLevel.Supervisor, []),
        new(
            RoleCodes.Administrator,
            "Administrador",
            RoleLevel.Administrador,
            PermissionDefinitions.Select(permission => permission.Code).ToArray())
    ];

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var permissionsByCode = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PermissionDefinitions)
        {
            var permission = await permissionRepository.GetByCodeAsync(definition.Code, cancellationToken);
            if (permission is null)
            {
                permission = Permission.Create(definition.Code, definition.Name, definition.Description, definition.Module, clock.UtcNow);
                await permissionRepository.AddAsync(permission, cancellationToken);
            }

            permissionsByCode[definition.Code] = permission;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var rolesByCode = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in RoleDefinitions)
        {
            var role = await roleRepository.GetByCodeAsync(definition.Code, cancellationToken);
            if (role is null)
            {
                role = Role.Create(definition.Code, definition.Name, definition.Level, clock.UtcNow);
                await roleRepository.AddAsync(role, cancellationToken);
            }

            rolesByCode[definition.Code] = role;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var definition in RoleDefinitions)
        {
            var role = rolesByCode[definition.Code];
            foreach (var permissionCode in definition.PermissionCodes)
            {
                var permission = permissionsByCode[permissionCode];
                role.GrantPermission(permission, clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private sealed record PermissionDefinition(string Code, string Name, string Description, string Module);

    private sealed record RoleDefinition(
        string Code,
        string Name,
        RoleLevel Level,
        IReadOnlyCollection<string> PermissionCodes);
}
