using System.Reflection;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class UserEffectivePermissionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task RoleSummary_StillShowsTemplateGrantsAndIndividualOverrides(bool? activeRole)
    {
        var roleId = Guid.NewGuid();
        var inherited = Permission("SALES.READ");
        var revoked = Permission("SALES.CREATE");
        var granted = Permission("PRODUCTS.COST_READ");
        var user = new Usuario
        {
            Id = Guid.NewGuid(),
            UsuarioRolUsuario = new UsuarioRol
            {
                RolId = roleId,
                // The general user query deliberately carries no role permissions.
                Rol = new Rol { Id = roleId, Codigo = "CAJERO", Activo = true }
            },
            UsuarioPermiso =
            [
                new() { PermisoId = revoked.Id, Permiso = revoked, Efecto = UserPermissionEffects.Revoke },
                new() { PermisoId = granted.Id, Permiso = granted, Efecto = UserPermissionEffects.Grant }
            ]
        };
        Rol? template = activeRole.HasValue ? new Rol
        {
            Id = roleId, Activo = activeRole.Value,
            RolPermiso =
            [
                new() { PermisoId = inherited.Id, Permiso = inherited },
                new() { PermisoId = revoked.Id, Permiso = revoked }
            ]
        } : null;

        var users = Stub<IUserRepository>.Create((method, args) =>
        {
            Assert.Equal(nameof(IUserRepository.GetByIdAsync), method.Name);
            Assert.Equal(user.Id, args![0]);
            return Task.FromResult<Usuario?>(user);
        });
        var roles = Stub<IRoleRepository>.Create((method, args) =>
        {
            Assert.Equal(nameof(IRoleRepository.GetByIdAsync), method.Name);
            Assert.Equal(roleId, args![0]);
            return Task.FromResult(template);
        });
        var permissions = Stub<IPermissionRepository>.Create((method, _) =>
        {
            Assert.Equal(nameof(IPermissionRepository.ListAsync), method.Name);
            return Task.FromResult(new PagedResult<Permiso>([inherited, revoked, granted], 1, 200, 3));
        });
        var service = new UserManagementService(users, roles, permissions,
            Unused<IPasswordHasher>(), Unused<IPermissionChecker>(), Unused<IAuditRepository>(),
            Unused<IUnitOfWork>(), Unused<IClock>());

        var result = await service.GetEffectivePermissionsAsync(
            new ActorContext(Guid.NewGuid(), RoleLevel.Administrador), user.Id);

        Assert.True(result.IsSuccess);
        var items = result.Value!.ToDictionary(item => item.Id);
        Assert.Equal(activeRole == true, items[inherited.Id].Granted);
        Assert.Equal("ROL", items[inherited.Id].Source);
        Assert.False(items[revoked.Id].Granted);
        Assert.Equal("INDIVIDUAL", items[revoked.Id].Source);
        Assert.True(items[granted.Id].Granted);
        Assert.Equal("INDIVIDUAL", items[granted.Id].Source);
        Assert.Equal(3, items.Count);
    }

    private static Permiso Permission(string code) => new()
    {
        Id = Guid.NewGuid(), Codigo = code, Nombre = code, Activo = true
    };

    private static T Unused<T>() where T : class => Stub<T>.Create((method, _) =>
        throw new InvalidOperationException($"Unexpected call: {method.Name}"));

    public class Stub<T> : DispatchProxy where T : class
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        public static T Create(Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = Create<T, Stub<T>>();
            ((Stub<T>)(object)proxy).Handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
