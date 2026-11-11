using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Layaway parameters (minimum initial percentage and default term): Administrator only, versioned and audited.</summary>
public sealed class LayawaySettingsApiTests : IAsyncLifetime
{
    private const string AdminName = "layaway.settings.admin.test";
    private const string AdminPassword = "Layaway-Settings-Admin-Test-2026!";
    private const string SupervisorName = "layaway.settings.supervisor.test";
    private const string SupervisorPassword = "Layaway-Settings-Supervisor-Test-2026!";
    private const string CashierName = "layaway.settings.cashier.test";
    private const string CashierPassword = "Layaway-Settings-Cashier-Test-2026!";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly LocalPostgresDatabase _postgres = new LocalPostgresDatabase();
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
            builder.UseSetting("Jwt:Issuer", "controlplus-layaway-settings-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-layaway-settings-client");
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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Administrator_ReadsAndUpdatesTheSettings_AndItIsVersionedAndAudited()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);

        var initial = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);
        Assert.Equal(20m, initial.MinimumPercentage);
        Assert.True(initial.Version > 0);

        var updated = await ReadAsync<LayawaySettingsDto>(await client.PutAsJsonAsync("/api/layaways/settings",
            new UpdateLayawaySettingsRequest(25.5m, 10, initial.Version)), HttpStatusCode.OK);
        Assert.Equal(25.5m, updated.MinimumPercentage);
        Assert.Equal(10, updated.DefaultDays);
        Assert.True(updated.Version > initial.Version);

        var reread = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);
        Assert.Equal(updated, reread);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Single(await db.EventoAuditoria.Where(x => x.Accion == "LAYAWAYSETTINGSUPDATED").ToArrayAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DefaultDays_CanBeCleared_SoTheDueDateIsChosenOnEachLayaway()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);

        var updated = await ReadAsync<LayawaySettingsDto>(await client.PutAsJsonAsync("/api/layaways/settings",
            new UpdateLayawaySettingsRequest(20m, null, initial.Version)), HttpStatusCode.OK);

        Assert.Null(updated.DefaultDays);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StaleVersion_IsAConcurrencyConflict_WithoutChanges()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<LayawaySettingsDto>(await client.PutAsJsonAsync("/api/layaways/settings",
            new UpdateLayawaySettingsRequest(30m, 12, initial.Version)), HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync("/api/layaways/settings",
            new UpdateLayawaySettingsRequest(40m, 20, initial.Version));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency.conflict");
        var current = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);
        Assert.Equal(30m, current.MinimumPercentage);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(100.01, 10)]
    [InlineData(33.333, 10)]
    [InlineData(20, 0)]
    [InlineData(20, 366)]
    public async Task InvalidValues_AreRejected_WithoutChanges(double percentage, int days)
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync("/api/layaways/settings",
            new UpdateLayawaySettingsRequest((decimal)percentage, days, initial.Version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(initial, await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SupervisorAndCashier_CannotReadOrChangeTheSettings()
    {
        using var client = CreateClient();
        var (admin, supervisor, cashier) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);

        foreach (var token in new[] { supervisor, cashier })
        {
            SetBearer(client, token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/layaways/settings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/layaways/settings",
                new UpdateLayawaySettingsRequest(50m, 5, initial.Version))).StatusCode);
        }

        SetBearer(client, admin);
        Assert.Equal(initial, await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UnknownProperties_AreRejected()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<LayawaySettingsDto>(await client.GetAsync("/api/layaways/settings"), HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync("/api/layaways/settings", new
        {
            minimumPercentage = 25m, defaultDays = 10, expectedVersion = initial.Version, unexpected = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Returns the access tokens of the Administrator, a Supervisor and a Cajero.</summary>
    private async Task<(string Admin, string Supervisor, string Cashier)> BootstrapAsync(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de apartados", "LAYAWAY-SETTINGS-TEST", "LAYAWAY-SETTINGS-INSTALLATION",
                   "LAYS1", "LAYAWAY-SETTINGS-TERMINAL", "Terminal ficticia de apartados",
                   AdminName, "Administrador ficticio de apartados", AdminPassword)) })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin);
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de apartados", SupervisorPassword, [supervisorRole])),
            HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de apartados", CashierPassword, [cashierRole])),
            HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierName, CashierPassword);
        return (admin, supervisor, cashier);
    }

    private static async Task<string> LoginAsync(HttpClient client, string name, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var result = await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(name, password)), HttpStatusCode.OK);
        return result.AccessToken;
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == code).Id;
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

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string RandomSecret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
