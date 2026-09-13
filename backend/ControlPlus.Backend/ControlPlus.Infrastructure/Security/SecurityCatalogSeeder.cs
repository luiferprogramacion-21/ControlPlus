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
    private static readonly RoleDefinition[] RoleDefinitions =
    [
        new(RoleCodes.Cashier, "Cajero", RoleLevel.Cajero),
        new(RoleCodes.Supervisor, "Supervisor", RoleLevel.Supervisor),
        new(RoleCodes.Administrator, "Administrador", RoleLevel.Administrador)
    ];

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var permissionsByCode = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in DefaultPermissionCatalog.Permissions)
        {
            var permission = await permissionRepository.GetByCodeAsync(definition.Code, cancellationToken);
            if (permission is null)
            {
                permission = Permission.Create(definition.Code, definition.Name, "Permiso estable de ControlPlus V1.", definition.Module, clock.UtcNow);
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
            foreach (var permissionCode in DefaultPermissionCatalog.ForRole(definition.Code))
            {
                var permission = permissionsByCode[permissionCode];
                role.GrantPermission(permission, clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private sealed record RoleDefinition(string Code, string Name, RoleLevel Level);
}
