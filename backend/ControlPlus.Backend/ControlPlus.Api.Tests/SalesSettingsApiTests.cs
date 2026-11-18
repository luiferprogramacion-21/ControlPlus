using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Establishments.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SalesSettingsApiTests : IAsyncLifetime
{
    private const string AdminName = "sales.settings.admin.test";
    private const string AdminPassword = "Sales-Settings-Admin-Test-2026!";
    private const string SupervisorName = "sales.settings.supervisor.test";
    private const string SupervisorPassword = "Sales-Settings-Supervisor-Test-2026!";
    private const string CashierName = "sales.settings.cashier.test";
    private const string CashierPassword = "Sales-Settings-Cashier-Test-2026!";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly LocalPostgresDatabase _postgres = new();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var context = new ControlPlusDbContext(
                         new DbContextOptionsBuilder<ControlPlusDbContext>()
                             .UseNpgsql(_postgres.GetConnectionString()).Options))
            await context.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-sales-settings-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-sales-settings-client");
            builder.UseSetting("Jwt:SigningKey", _jwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Administrator_ChangesTheWholesaleMinimum_VersionedAndAudited()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK);
        Assert.Equal(12, initial.WholesaleMinimumQuantity);

        var updated = await ReadAsync<SalesSettingsDto>(await client.PutAsJsonAsync(
            "/api/sales/settings", new UpdateSalesSettingsRequest(6, initial.Version)), HttpStatusCode.OK);
        Assert.Equal(6, updated.WholesaleMinimumQuantity);
        Assert.True(updated.Version > initial.Version);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.Accion == "SALESSETTINGSUPDATED");
        using var data = JsonDocument.Parse(audit.DatosNuevos!);
        Assert.Equal(12, data.RootElement.GetProperty("Previous").GetProperty("WholesaleMinimumQuantity").GetInt32());
        Assert.Equal(6, data.RootElement.GetProperty("New").GetProperty("WholesaleMinimumQuantity").GetInt32());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task EstablishmentData_AreReadableByTheOperation_AndOnlyAdministratorUpdatesThem()
    {
        using var client = CreateClient();
        var (admin, supervisor, cashier) = await BootstrapAsync(client);

        SetBearer(client, cashier);
        var visible = await ReadAsync<EstablishmentDto>(
            await client.GetAsync("/api/establishment"), HttpStatusCode.OK);
        Assert.Equal("Comercio ficticio de ventas", visible.CommercialName);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/establishment",
            new UpdateEstablishmentRequest("No permitido", "NIT-X", null, null, null, null,
                visible.Version))).StatusCode);

        SetBearer(client, admin);
        var updated = await ReadAsync<EstablishmentDto>(await client.PutAsJsonAsync(
            "/api/establishment", new UpdateEstablishmentRequest(
                "Tienda Central", "901234567-8", "3150000000", "Ocaña, Norte de Santander",
                "Compra local", "Gracias por su compra", visible.Version)), HttpStatusCode.OK);
        Assert.Equal("Tienda Central", updated.CommercialName);
        Assert.Equal("901234567-8", updated.Identification);
        Assert.True(updated.Version > visible.Version);

        SetBearer(client, supervisor);
        Assert.Equal("Tienda Central", (await ReadAsync<EstablishmentDto>(
            await client.GetAsync("/api/establishment"), HttpStatusCode.OK)).CommercialName);

        SetBearer(client, admin);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/establishment", new
        {
            commercialName = "Tienda Central", identification = "901234567-8",
            expectedVersion = updated.Version, email = "no-existe@controlplus.local"
        }), HttpStatusCode.BadRequest, "validation.failed");

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var establishment = await db.Establecimiento.AsNoTracking().SingleAsync();
        Assert.Equal("3150000000", establishment.Telefono);
        Assert.Equal("Compra local", establishment.EncabezadoComprobante);
        Assert.Equal("Gracias por su compra", establishment.PieComprobante);
        var audit = await db.EventoAuditoria.AsNoTracking()
            .SingleAsync(x => x.Accion == "ESTABLISHMENTUPDATED");
        using var data = JsonDocument.Parse(audit.DatosNuevos!);
        Assert.Equal("Comercio ficticio de ventas",
            data.RootElement.GetProperty("Previous").GetProperty("CommercialName").GetString());
        Assert.Equal("Tienda Central",
            data.RootElement.GetProperty("New").GetProperty("CommercialName").GetString());
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(1, true)]
    [InlineData(999, true)]
    [InlineData(0, false)]
    [InlineData(1000, false)]
    public async Task Range_IsOneToNineHundredNinetyNine(int value, bool accepted)
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK);
        var response = await client.PutAsJsonAsync("/api/sales/settings", new UpdateSalesSettingsRequest(value, initial.Version));
        if (accepted)
            Assert.Equal(value, (await ReadAsync<SalesSettingsDto>(response, HttpStatusCode.OK)).WholesaleMinimumQuantity);
        else
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
            Assert.Equal(initial, await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK));
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task StaleVersion_IsAConcurrencyConflictWithoutChanges()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<SalesSettingsDto>(await client.PutAsJsonAsync(
            "/api/sales/settings", new UpdateSalesSettingsRequest(10, initial.Version)), HttpStatusCode.OK);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/sales/settings",
            new UpdateSalesSettingsRequest(20, initial.Version)), HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal(10, (await ReadAsync<SalesSettingsDto>(
            await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK)).WholesaleMinimumQuantity);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SupervisorAndCashier_CannotReadOrUpdate_ButCanReadThePricingRules()
    {
        using var client = CreateClient();
        var (admin, supervisor, cashier) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<SalesSettingsDto>(await client.PutAsJsonAsync(
            "/api/sales/settings", new UpdateSalesSettingsRequest(5, initial.Version)), HttpStatusCode.OK);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.ConfigurationManage);
        var users = await ReadAsync<PagedResult<UserDto>>(await client.GetAsync("/api/users?pageSize=100"), HttpStatusCode.OK);
        foreach (var userName in new[] { SupervisorName, CashierName })
        {
            var userId = users.Items.Single(x => x.UserName == userName).Id;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/users/{userId}/permissions/{permissionId}", new SetUserPermissionRequest(true))).StatusCode);
        }
        supervisor = await LoginAsync(client, SupervisorName, SupervisorPassword);
        cashier = await LoginAsync(client, CashierName, CashierPassword);
        foreach (var token in new[] { supervisor, cashier })
        {
            SetBearer(client, token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sales/settings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
                "/api/sales/settings", new UpdateSalesSettingsRequest(7, initial.Version + 1))).StatusCode);
            // Quien vende conoce el mínimo vigente para avisar antes de confirmar.
            Assert.Equal(5, (await ReadAsync<SalesPricingRulesDto>(
                await client.GetAsync("/api/sales/pricing-rules"), HttpStatusCode.OK)).WholesaleMinimumQuantity);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UnknownOrMissingProperties_AreRejected()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/sales/settings",
            new { wholesaleMinimumQuantity = 12, expectedVersion = initial.Version, unexpected = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            "/api/sales/settings", new { expectedVersion = initial.Version })).StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(string Admin, string Supervisor, string Cashier)> BootstrapAsync(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de ventas", "SALES-SETTINGS-TEST", "SALES-SETTINGS-INSTALLATION",
                   "SLS1", "SALES-SETTINGS-TERMINAL", "Terminal ficticia de ventas",
                   AdminName, "Administrador ficticio de ventas", AdminPassword)) })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin);
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de ventas",
                SupervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de ventas",
                CashierPassword, [cashierRole])), HttpStatusCode.Created);
        return (admin,
            await LoginAsync(client, SupervisorName, SupervisorPassword),
            await LoginAsync(client, CashierName, CashierPassword));
    }

    private static async Task<string> LoginAsync(HttpClient client, string name, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return (await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(name, password)), HttpStatusCode.OK)).AccessToken;
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string code)
    {
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} ({expected}) but received {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        return value;
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string RandomSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
