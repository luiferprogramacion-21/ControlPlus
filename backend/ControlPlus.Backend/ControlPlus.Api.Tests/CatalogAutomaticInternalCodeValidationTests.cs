using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests
{
    [Theory, Trait("Category", "Integration")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingInternalCode_IsAssignedPermanentReferenceAndAudited(string? code)
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var product = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, code)));
        Assert.Matches("^CAT-[0-9]{6,}$", product.InternalCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var stored = await db.Producto.AsNoTracking().SingleAsync();
        Assert.Equal(product.Id, stored.Id);
        Assert.Equal(product.InternalCode, stored.CodigoInterno);
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.EntidadId == product.Id);
        Assert.Contains(product.InternalCode, audit.DatosNuevos);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ManualInternalCode_NormalizesAndRejectsDuplicatesAndExcessLengthWithoutWrites()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var product = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, "  manual-01  ")));
        Assert.Matches("^CAT-[0-9]{6,}$", product.InternalCode);
        var found = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=MANUAL-01");
        Assert.Equal(product.Id, Assert.Single(found!.Items).Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var audits = await db.EventoAuditoria.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, "manual-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, new string('X', 51)))).StatusCode);
        Assert.Equal(1, await db.Producto.CountAsync());
        Assert.Equal(audits, await db.EventoAuditoria.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentManualInternalCode_OneCreationOneConflictAndOneAudit()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, "MANUAL-CONCURRENT");
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/products", request), client.PostAsJsonAsync("/api/products", request));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var body = await conflict.Content.ReadAsStringAsync();
        Assert.DoesNotContain("23505", body);
        Assert.DoesNotContain("uq_producto", body);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.AsNoTracking().SingleAsync();
        Assert.Matches("^CAT-[0-9]{6,}$", product.CodigoInterno);
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.EntidadId == product.Id));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AutomaticInternalCode_RespectsCreatePermissionAndRevocation(bool supervisor)
    {
        var scenario = await CreateValidationScenarioAsync();
        using var manager = scenario.Client;
        var roles = await manager.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20");
        var roleId = roles!.Items.Single(x => x.Code == (supervisor ? RoleCodes.Supervisor : RoleCodes.Cashier)).Id;
        const string userName = "catalog.permission.test";
        const string password = "Catalog-Permission-Test-2026!";
        var response = await manager.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "Operador de prueba", password, [roleId]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        using var actor = _factory!.CreateClient();
        await LoginValidationActorAsync(actor, userName, password);
        var request = ProductRequest(scenario, null);
        Assert.Equal(supervisor ? HttpStatusCode.Created : HttpStatusCode.Forbidden,
            (await actor.PostAsJsonAsync("/api/products", request)).StatusCode);
        var permissions = (await manager.GetFromJsonAsync<PagedResult<PermissionDto>>("/api/permissions?page=1&pageSize=100"))!;
        var permissionId = permissions.Items.Single(x => x.Code == PermissionCodes.ProductsCreate).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/users/{user.Id}/permissions/{permissionId}", new SetUserPermissionRequest(true))).StatusCode);
        await LoginValidationActorAsync(actor, userName, password);
        Assert.Equal(HttpStatusCode.Created, (await actor.PostAsJsonAsync("/api/products", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/users/{user.Id}/permissions/{permissionId}", new SetUserPermissionRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await actor.PostAsJsonAsync("/api/products", request)).StatusCode);
        await LoginValidationActorAsync(actor, userName, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync("/api/products", request)).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UpdateAutomaticInternalCode_StillRequiresCodePermissionAndCurrentVersion()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var product = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, null)));
        var update = new UpdateProductRequest(scenario.CategoryId, scenario.UnitId, null, product.InternalCode,
            null, null, false, "Producto editado", null, 1000m, null, null, 0, product.Version);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{product.Id}", update with { InternalCode = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{product.Id}", update with { InternalCode = null! })).StatusCode);
        var changed = await client.PutAsJsonAsync($"/api/products/{product.Id}", update);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var updated = (await changed.Content.ReadFromJsonAsync<ProductDto>())!;
        Assert.Equal(product.InternalCode, updated.InternalCode);
        Assert.Equal(product.Version + 1, updated.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/products/{product.Id}", update)).StatusCode);
        // A Supervisor can create, but still cannot edit codes or prices by default.
        var roles = (await client.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        var role = roles.Items.Single(x => x.Code == RoleCodes.Supervisor);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest("catalog.update.supervisor", "Supervisor de prueba", "Catalog-Update-Test-2026!", [role.Id]))).StatusCode);
        using var supervisor = _factory!.CreateClient();
        await LoginValidationActorAsync(supervisor, "catalog.update.supervisor", "Catalog-Update-Test-2026!");
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PutAsJsonAsync($"/api/products/{product.Id}", update with { Version = updated.Version })).StatusCode);
    }

    private async Task<ValidationScenario> CreateValidationScenarioAsync()
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest("Catálogo temporal", "CATALOG-VALIDATION", "CATALOG-VALIDATION-INSTALL", "CATVAL1",
                "CATALOG-TERM", "Terminal temporal", AdminName, "Administrador temporal", AdminPassword))
        };
        setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        await LoginValidationActorAsync(client, AdminName, AdminPassword);
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Categoría temporal", null, "CAT"));
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>())!;
        var units = (await client.GetFromJsonAsync<MeasurementUnitDto[]>("/api/measurement-units"))!;
        return new ValidationScenario(client, category.Id, units.Single(x => x.Code == "UNIDAD").Id);
    }

    private static CreateProductRequest ProductRequest(ValidationScenario scenario, string? code) =>
        new(scenario.CategoryId, scenario.UnitId, null, code, null, null, false, "Producto temporal", null, 1000m, null, null, 0);

    private static async Task LoginValidationActorAsync(HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthenticationResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    private static async Task<ProductDto> ReadProductAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<ProductDto>(await response.Content.ReadFromJsonAsync<ProductDto>());
    }

    private sealed record ValidationScenario(HttpClient Client, Guid CategoryId, Guid UnitId);
}
