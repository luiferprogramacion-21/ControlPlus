using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
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

public sealed class CreditSettingsApiTests : IAsyncLifetime
{
    private const string AdminName = "credit.settings.admin.test";
    private const string AdminPassword = "Credit-Settings-Admin-Test-2026!";
    private const string SupervisorName = "credit.settings.supervisor.test";
    private const string SupervisorPassword = "Credit-Settings-Supervisor-Test-2026!";
    private const string CashierName = "credit.settings.cashier.test";
    private const string CashierPassword = "Credit-Settings-Cashier-Test-2026!";

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
            builder.UseSetting("Jwt:Issuer", "controlplus-credit-settings-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-credit-settings-client");
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
    public async Task Administrator_UpdatesAndClearsDefault_AndChangeIsVersionedAndAudited()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);

        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        Assert.Equal(30, initial.DefaultDays);
        Assert.True(initial.Version > 0);

        var updated = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(45, initial.Version)),
            HttpStatusCode.OK);
        Assert.Equal(45, updated.DefaultDays);
        Assert.True(updated.Version > initial.Version);

        var cleared = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(null, updated.Version)),
            HttpStatusCode.OK);
        Assert.Null(cleared.DefaultDays);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var audits = await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.Accion == "CREDITSETTINGSUPDATED")
            .OrderBy(x => x.FechaHora).ToArrayAsync();
        Assert.Equal(2, audits.Length);
        using var auditData = JsonDocument.Parse(audits[0].DatosNuevos!);
        Assert.Equal(30, auditData.RootElement.GetProperty("Previous")
            .GetProperty("DefaultDays").GetInt32());
        Assert.Equal(45, auditData.RootElement.GetProperty("New")
            .GetProperty("DefaultDays").GetInt32());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Administrator_ConfiguresCalendarMonths_AndSalesReadTheSameRule()
    {
        using var client = CreateClient();
        var (admin, _, cashier) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        Assert.Equal(30, initial.DefaultDays);
        Assert.Null(initial.DefaultMonths);

        var updated = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(null, initial.Version,
                DefaultMonths: 3)), HttpStatusCode.OK);
        Assert.Null(updated.DefaultDays);
        Assert.Equal(3, updated.DefaultMonths);
        Assert.True(updated.Version > initial.Version);

        SetBearer(client, cashier);
        var rules = await ReadAsync<CreditRulesDto>(
            await client.GetAsync("/api/sales/credit-rules"), HttpStatusCode.OK);
        Assert.Null(rules.DefaultDays);
        Assert.Equal(3, rules.DefaultMonths);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var audit = await db.EventoAuditoria.AsNoTracking()
            .SingleAsync(x => x.Accion == "CREDITSETTINGSUPDATED");
        using var data = JsonDocument.Parse(audit.DatosNuevos!);
        Assert.Equal(30, data.RootElement.GetProperty("Previous").GetProperty("DefaultDays").GetInt32());
        Assert.Equal(3, data.RootElement.GetProperty("New").GetProperty("DefaultMonths").GetInt32());
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(0)]
    [InlineData(7)] // Decisión D1 del 23/11/2026: hasta 6 meses.
    public async Task InvalidCalendarMonths_AreRejectedWithoutChangingSettings(int months)
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/credits/settings",
            new UpdateCreditSettingsRequest(null, initial.Version, DefaultMonths: months)),
            HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(initial, await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DaysAndMonthsTogether_AreRejected()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/credits/settings",
            new UpdateCreditSettingsRequest(30, initial.Version, DefaultMonths: 3)),
            HttpStatusCode.BadRequest, "validation.failed");
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(1)]
    [InlineData(90)]
    public async Task BoundaryValues_AreAccepted(int days)
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        var updated = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(days, initial.Version)),
            HttpStatusCode.OK);
        Assert.Equal(days, updated.DefaultDays);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(0)]
    [InlineData(181)] // Decisión D1 del 23/11/2026: hasta 180 días.
    public async Task ValuesOutsideRange_AreRejectedWithoutChanges(int days)
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);

        await AssertProblemAsync(await client.PutAsJsonAsync("/api/credits/settings",
            new UpdateCreditSettingsRequest(days, initial.Version)),
            HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(initial, await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task StaleVersion_IsAConcurrencyConflictWithoutChanges()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(40, initial.Version)),
            HttpStatusCode.OK);

        await AssertProblemAsync(await client.PutAsJsonAsync("/api/credits/settings",
            new UpdateCreditSettingsRequest(50, initial.Version)),
            HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.Equal(40, (await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK)).DefaultDays);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SupervisorAndCashier_CannotReadOrUpdateEvenWithConfigurationPermission()
    {
        using var client = CreateClient();
        var (admin, supervisor, cashier) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.ConfigurationManage);
        var users = await ReadAsync<PagedResult<UserDto>>(
            await client.GetAsync("/api/users?pageSize=100"), HttpStatusCode.OK);
        foreach (var userName in new[] { SupervisorName, CashierName })
        {
            var userId = users.Items.Single(x => x.UserName == userName).Id;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/users/{userId}/permissions/{permissionId}",
                new SetUserPermissionRequest(true))).StatusCode);
        }
        supervisor = await LoginAsync(client, SupervisorName, SupervisorPassword);
        cashier = await LoginAsync(client, CashierName, CashierPassword);

        foreach (var token in new[] { supervisor, cashier })
        {
            SetBearer(client, token);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/credits/settings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
                "/api/credits/settings", new UpdateCreditSettingsRequest(35, initial.Version))).StatusCode);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SalesRules_RequireCreditRequestOnly_AndReturnNullWhenConfigured()
    {
        using var client = CreateClient();
        var (admin, _, cashier) = await BootstrapAsync(client);
        SetBearer(client, cashier);
        var initialRules = await ReadAsync<CreditRulesDto>(
            await client.GetAsync("/api/sales/credit-rules"), HttpStatusCode.OK);
        Assert.Equal(30, initialRules.DefaultDays);

        SetBearer(client, admin);
        var settings = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(null, settings.Version)),
            HttpStatusCode.OK);

        SetBearer(client, cashier);
        Assert.Null((await ReadAsync<CreditRulesDto>(
            await client.GetAsync("/api/sales/credit-rules"), HttpStatusCode.OK)).DefaultDays);

        SetBearer(client, admin);
        var users = await ReadAsync<PagedResult<UserDto>>(
            await client.GetAsync("/api/users?pageSize=100"), HttpStatusCode.OK);
        var cashierId = users.Items.Single(x => x.UserName == CashierName).Id;
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.CreditRequest);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{cashierId}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        cashier = await LoginAsync(client, CashierName, CashierPassword);
        SetBearer(client, cashier);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/sales/credit-rules")).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task UnknownOrMissingProperties_AreRejected()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            "/api/credits/settings",
            new { defaultDays = 30, expectedVersion = initial.Version, unexpected = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            "/api/credits/settings", new { expectedVersion = initial.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            "/api/credits/settings", new { defaultDays = 30, expectedVersion = initial.Version })).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SettingsAndSalesRules_ReturnNotFoundWithoutActiveInstallation()
    {
        using var client = CreateClient();
        var (admin, _, cashier) = await BootstrapAsync(client);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var installation = await db.Instalacion.SingleAsync();
            installation.Activo = false;
            await db.SaveChangesAsync();
        }

        SetBearer(client, admin);
        await AssertProblemAsync(await client.GetAsync("/api/credits/settings"),
            HttpStatusCode.NotFound, "resource.not_found");
        SetBearer(client, cashier);
        await AssertProblemAsync(await client.GetAsync("/api/sales/credit-rules"),
            HttpStatusCode.NotFound, "resource.not_found");
    }

    private HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(string Admin, string Supervisor, string Cashier)> BootstrapAsync(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de créditos", "CREDIT-SETTINGS-TEST", "CREDIT-SETTINGS-INSTALLATION",
                   "CRS1", "CREDIT-SETTINGS-TERMINAL", "Terminal ficticia de créditos",
                   AdminName, "Administrador ficticio de créditos", AdminPassword)) })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin);
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de créditos",
                SupervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de créditos",
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
        var value = JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web);
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
