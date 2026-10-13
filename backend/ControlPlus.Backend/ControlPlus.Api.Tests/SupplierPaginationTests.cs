using System.Reflection;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Application.Suppliers.Ports;
using ControlPlus.Application.Suppliers.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SupplierPaginationTests
{
    [Fact]
    public async Task ExtremePage_IsRejectedBeforeSupplierQueriesOrWrites()
    {
        var userId = Guid.CreateVersion7();
        var users = DispatchProxy.Create<IUserRepository, AuthorizationProxy>();
        ((AuthorizationProxy)(object)users).Snapshot = new UserAuthorizationSnapshot(userId, "test", "Test", "stamp",
            RoleLevel.Administrador, [], [new PermissionClaim(Guid.CreateVersion7(), PermissionCodes.SuppliersRead, "Read")]);
        var repository = new SupplierSpy();
        var service = new SupplierService(repository, users, null!, null!, null!);
        var result = await service.ListAsync(new ActorContext(userId, RoleLevel.Administrador),
            new SupplierListQuery(Page: int.MaxValue, PageSize: 100));
        Assert.True(result.IsFailure);
        Assert.Equal("validation.failed", result.Error!.Code);
        Assert.Equal(0, repository.Calls);
    }

    public class AuthorizationProxy : DispatchProxy
    {
        public UserAuthorizationSnapshot? Snapshot { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IUserRepository.GetAuthorizationSnapshotAsync)
                ? Task.FromResult(Snapshot)
                : throw new InvalidOperationException("Unexpected user repository call.");
    }

    private sealed class SupplierSpy : ISupplierRepository
    {
        public int Calls { get; private set; }
        public Task<PagedResult<Proveedor>> ListAsync(SupplierListQuery query, int offset, CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("Extreme page queried suppliers."); }
        public Task<Proveedor?> GetAsync(Guid id, CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("Extreme page queried suppliers."); }
        public Task AddAsync(Proveedor supplier, CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("Extreme page wrote a supplier."); }
        public Task<ISupplierTransaction> BeginAsync(CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("Extreme page began a transaction."); }
    }
}
