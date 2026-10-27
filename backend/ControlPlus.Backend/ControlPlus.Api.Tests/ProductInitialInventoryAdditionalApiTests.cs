using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task InitialInventory_ReplayPreservesOriginalCostVisibilityAndRedactsAfterRevocation()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var manager = scenario.Client;
        var roles = (await manager.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        var permissions = (await manager.GetFromJsonAsync<PagedResult<PermissionDto>>("/api/permissions?page=1&pageSize=100"))!;
        var roleId = roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id;
        var costsPermissionId = permissions.Items.Single(x => x.Code == PermissionCodes.ProductCostsRead).Id;
        const string userName = "catalog.initial.cost.visibility";
        const string password = "Catalog-Initial-Cost-2026!";
        using var userResponse = await manager.PostAsJsonAsync("/api/users",
            new CreateUserRequest(userName, "Supervisor de costo", password, [roleId]));
        Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
        var user = (await userResponse.Content.ReadFromJsonAsync<UserDto>())!;

        using var supervisor = _factory!.CreateClient();
        await LoginValidationActorAsync(supervisor, userName, password);
        var hiddenRequest = ProductRequest(scenario, "INITIAL-HIDDEN-COST") with
        {
            InitialQuantity = 2, InitialUnitCost = 100m
        };
        using var firstHidden = await PostInitialAsync(supervisor, hiddenRequest, "initial-hidden-cost");
        var originalHidden = await ReadProductAsync(firstHidden);
        Assert.False(originalHidden.CanViewCost);
        Assert.Null(originalHidden.AverageCost);

        using var grant = await manager.PutAsJsonAsync($"/api/users/{user.Id}/permissions/{costsPermissionId}",
            new SetUserPermissionRequest(true));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        await LoginValidationActorAsync(supervisor, userName, password);
        using var hiddenReplay = await PostInitialAsync(supervisor, hiddenRequest, "initial-hidden-cost");
        Assert.Equal(originalHidden, await ReadProductAsync(hiddenReplay));

        var visibleRequest = ProductRequest(scenario, "INITIAL-VISIBLE-COST") with
        {
            InitialQuantity = 3, InitialUnitCost = 250m
        };
        using var firstVisible = await PostInitialAsync(supervisor, visibleRequest, "initial-visible-cost");
        var originalVisible = await ReadProductAsync(firstVisible);
        Assert.True(originalVisible.CanViewCost);
        Assert.Equal(250m, originalVisible.AverageCost);

        using var revoke = await manager.PutAsJsonAsync($"/api/users/{user.Id}/permissions/{costsPermissionId}",
            new SetUserPermissionRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        await LoginValidationActorAsync(supervisor, userName, password);
        using var redactedReplay = await PostInitialAsync(supervisor, visibleRequest, "initial-visible-cost");
        Assert.Equal(originalVisible with { AverageCost = null, CanViewCost = false },
            await ReadProductAsync(redactedReplay));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(2, await db.Producto.CountAsync());
        Assert.Equal(2, await db.MovimientoInventario.CountAsync());
        Assert.Equal(2, await db.OperacionIdempotente.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task InitialInventory_ConcurrentSameKeyAndLaterReplay_ReturnOriginalSnapshotOnce()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, "INITIAL-CONCURRENT") with { InitialQuantity = 5, InitialUnitCost = 125.5m };
        var responses = await Task.WhenAll(
            PostInitialAsync(client, request, "initial-concurrent"),
            PostInitialAsync(client, request, "initial-concurrent"));
        using var first = responses[0];
        using var second = responses[1];
        var product = await ReadProductAsync(first);
        Assert.Equal(product, await ReadProductAsync(second));

        var update = new UpdateProductRequest(scenario.CategoryId, scenario.UnitId, null, product.InternalCode,
            null, null, false, "Nombre editado", null, product.RetailPrice, null, null, 0, product.Version);
        using var updated = await client.PutAsJsonAsync($"/api/products/{product.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var replay = await PostInitialAsync(client, request, "initial-concurrent");
        Assert.Equal(product, await ReadProductAsync(replay));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Producto.CountAsync());
        Assert.Equal(1, await db.MovimientoInventario.CountAsync(x => x.ProductoId == product.Id));
        Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.EntidadId == product.Id));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.EntidadId == product.Id && x.Accion == "PRODUCTCREATED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task InitialInventory_RequiresEligibleRoleAndCurrentCreatePermissionEvenForReplay()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var manager = scenario.Client;
        var roles = (await manager.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        var permissions = (await manager.GetFromJsonAsync<PagedResult<PermissionDto>>("/api/permissions?page=1&pageSize=100"))!;
        var createPermission = permissions.Items.Single(x => x.Code == PermissionCodes.ProductsCreate).Id;
        var supervisorRole = roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id;
        var cashierRole = roles.Items.Single(x => x.Code == RoleCodes.Cashier).Id;
        const string supervisorName = "catalog.initial.revoked";
        const string cashierName = "catalog.initial.cashier";
        const string password = "Catalog-Initial-Role-2026!";
        using var supervisorCreated = await manager.PostAsJsonAsync("/api/users",
            new CreateUserRequest(supervisorName, "Supervisor", password, [supervisorRole]));
        using var cashierCreated = await manager.PostAsJsonAsync("/api/users",
            new CreateUserRequest(cashierName, "Cajero", password, [cashierRole]));
        Assert.Equal(HttpStatusCode.Created, supervisorCreated.StatusCode);
        Assert.Equal(HttpStatusCode.Created, cashierCreated.StatusCode);
        var supervisorUser = (await supervisorCreated.Content.ReadFromJsonAsync<UserDto>())!;
        var cashierUser = (await cashierCreated.Content.ReadFromJsonAsync<UserDto>())!;

        using var supervisor = _factory!.CreateClient();
        await LoginValidationActorAsync(supervisor, supervisorName, password);
        var request = ProductRequest(scenario, "INITIAL-ROLE") with { InitialQuantity = 2, InitialUnitCost = 100m };
        using var initial = await PostInitialAsync(supervisor, request, "initial-role-supervisor");
        var product = await ReadProductAsync(initial);
        Assert.Null(product.AverageCost);
        Assert.False(product.CanViewCost);
        using var otherActor = await PostInitialAsync(manager, request, "initial-role-supervisor");
        Assert.Equal(HttpStatusCode.Conflict, otherActor.StatusCode);
        using var deniedChange = await manager.PutAsJsonAsync($"/api/users/{supervisorUser.Id}/permissions/{createPermission}",
            new SetUserPermissionRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, deniedChange.StatusCode);
        await LoginValidationActorAsync(supervisor, supervisorName, password);
        using var forbiddenReplay = await PostInitialAsync(supervisor, request, "initial-role-supervisor");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReplay.StatusCode);

        using var grant = await manager.PutAsJsonAsync($"/api/users/{cashierUser.Id}/permissions/{createPermission}",
            new SetUserPermissionRequest(true));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        using var cashier = _factory!.CreateClient();
        await LoginValidationActorAsync(cashier, cashierName, password);
        using var forbiddenCashier = await PostInitialAsync(cashier,
            request with { InternalCode = "INITIAL-CASHIER" }, "initial-role-cashier");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCashier.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Producto.CountAsync());
        Assert.Equal(1, await db.MovimientoInventario.CountAsync());
        Assert.Equal(1, await db.OperacionIdempotente.CountAsync());
    }
}
