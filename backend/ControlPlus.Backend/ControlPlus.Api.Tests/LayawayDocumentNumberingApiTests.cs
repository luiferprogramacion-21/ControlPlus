using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class LayawayDocumentNumberingApiTests : IAsyncLifetime
{
    private const string LayawaysRoute = "/api/document-numbering/layaways";
    private const string PaymentsRoute = "/api/document-numbering/layaway-payments";
    private const string AdminUser = "layaway.numbering.admin";
    private const string AdminPassword = "Layaway-Numbering-Administrator-2026!";
    private const string SupervisorUser = "layaway.numbering.supervisor";
    private const string SupervisorPassword = "Layaway-Numbering-Supervisor-2026!";

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
            builder.UseSetting("Jwt:Issuer", "controlplus-layaway-numbering-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-layaway-numbering-client");
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
    public async Task Setup_CreatesEachApprovedSequence_WithoutAnyConfiguration()
    {
        using var client = CreateClient();
        SetBearer(client, (await SetupAndLoginAsync(client)).AccessToken);

        foreach (var (route, documentType, prefix) in new[]
                 { (LayawaysRoute, "APARTADO", "APT"), (PaymentsRoute, "PAGO_APARTADO", "PAP") })
        {
            var numbering = await ReadAsync<SalesDocumentNumberingDto>(
                await client.GetAsync(route), HttpStatusCode.OK);
            Assert.Equal(documentType, numbering.DocumentType);
            Assert.Equal(prefix, numbering.Prefix);
            Assert.Equal(0, numbering.LastNumber);
            Assert.Equal(1, numbering.NextNumber);
            Assert.Equal(1, numbering.Version);
            // It already exists, so creating it explicitly is a conflict.
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route,
                new CreateLayawayDocumentNumberingRequest(prefix, 1))).StatusCode);
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await db.EventoAuditoria.CountAsync(x =>
            x.Accion == "LAYAWAYNUMBERINGCREATED" ||
            x.Accion == "LAYAWAYPAYMENTNUMBERINGCREATED"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExplicitCreation_RepairsAMissingSequenceOnce_AndAudits()
    {
        using var client = CreateClient();
        SetBearer(client, (await SetupAndLoginAsync(client)).AccessToken);
        await RemoveSequencesAsync();

        foreach (var (route, prefix) in new[]
                 { (LayawaysRoute, "APT"), (PaymentsRoute, "PAP") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route,
                new CreateLayawayDocumentNumberingRequest("OTHER", 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route,
                new CreateLayawayDocumentNumberingRequest(prefix, 2))).StatusCode);

            using var unknownField = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { prefix, firstNumber = 1,
                        installationId = Guid.CreateVersion7() }), Encoding.UTF8, "application/json")
            };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(unknownField)).StatusCode);
        }

        await AssertNumberingAsync(client, LayawaysRoute, "APARTADO", "APT");
        await AssertNumberingAsync(client, PaymentsRoute, "PAGO_APARTADO", "PAP");

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var rows = await db.ConsecutivoDocumento.AsNoTracking()
            .Where(x => x.TipoDocumento == "APARTADO" || x.TipoDocumento == "PAGO_APARTADO")
            .OrderBy(x => x.TipoDocumento).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal(0, row.UltimoNumero);
            Assert.Equal(1, row.Version);
        });
        Assert.Equal(2, await db.EventoAuditoria.CountAsync(x =>
            x.Accion == "LAYAWAYNUMBERINGCREATED" ||
            x.Accion == "LAYAWAYPAYMENTNUMBERINGCREATED"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdministratorAndEffectivePermission_AreBothRequiredForInitialization()
    {
        using var client = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(LayawaysRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(PaymentsRoute,
            new CreateLayawayDocumentNumberingRequest("PAP", 1))).StatusCode);

        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        var supervisorRoleId = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorUser, "Supervisor ficticio de apartados",
                SupervisorPassword, [supervisorRoleId])), HttpStatusCode.Created);
        var users = await ReadAsync<PagedResult<UserDto>>(
            await client.GetAsync("/api/users?page=1&pageSize=100"), HttpStatusCode.OK);
        var supervisorId = users.Items.Single(x => x.UserName == SupervisorUser).Id;
        var adminId = users.Items.Single(x => x.UserName == AdminUser).Id;
        var numberingPermissionId = await GetPermissionIdAsync(client, PermissionCodes.NumberingManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{supervisorId}/permissions/{numberingPermissionId}",
            new SetUserPermissionRequest(true))).StatusCode);

        var supervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        SetBearer(client, supervisor.AccessToken);
        foreach (var (route, prefix) in new[]
                 { (LayawaysRoute, "APT"), (PaymentsRoute, "PAP") })
        {
            // The effective permission lets the Supervisor read it; only an Administrator may create it.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route,
                new CreateLayawayDocumentNumberingRequest(prefix, 1))).StatusCode);
        }

        SetBearer(client, admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{adminId}/permissions/{numberingPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        SetBearer(client, (await LoginAsync(client, AdminUser, AdminPassword)).AccessToken);
        foreach (var (route, prefix) in new[]
                 { (LayawaysRoute, "APT"), (PaymentsRoute, "PAP") })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route,
                new CreateLayawayDocumentNumberingRequest(prefix, 1))).StatusCode);
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        // Nobody created anything explicitly: only the installation's own rows exist.
        Assert.Equal(DocumentNumberingDefaults.All.Count, await db.ConsecutivoDocumento.CountAsync());
        Assert.Equal(0, await db.EventoAuditoria.CountAsync(x =>
            x.Accion == "LAYAWAYNUMBERINGCREATED" ||
            x.Accion == "LAYAWAYPAYMENTNUMBERINGCREATED"));
    }

    /// <summary>Simulates an installation created before the consecutives were automatic.</summary>
    private async Task RemoveSequencesAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM configuracion.consecutivo_documento " +
            "WHERE tipo_documento IN ('APARTADO', 'PAGO_APARTADO')", connection);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertNumberingAsync(
        HttpClient client, string route, string documentType, string prefix)
    {
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            route, new CreateLayawayDocumentNumberingRequest(prefix.ToLowerInvariant(), 1)),
            HttpStatusCode.Created);
        Assert.Equal(documentType, created.DocumentType);
        Assert.Equal(prefix, created.Prefix);
        Assert.Equal(0, created.LastNumber);
        Assert.Equal(1, created.NextNumber);
        Assert.Equal(1, created.Version);
        Assert.Equal(created, await ReadAsync<SalesDocumentNumberingDto>(
            await client.GetAsync(route), HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route,
            new CreateLayawayDocumentNumberingRequest(prefix, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync(route,
            new { prefix, firstNumber = 2 })).StatusCode);
    }

    private HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    private async Task<AuthenticationResult> SetupAndLoginAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                "Comercio ficticio de numeración de apartados", "LAYAWAY-NUMBERING-TEST",
                "LAYAWAY-NUMBERING-INSTALLATION", "NUMBER1", "LAYAWAY-NUMBERING-TERMINAL",
                "Terminal ficticia de numeración de apartados", AdminUser,
                "Administrador ficticio de apartados", AdminPassword))
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        return await LoginAsync(client, AdminUser, AdminPassword);
    }

    private static async Task<AuthenticationResult> LoginAsync(
        HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(userName, password)), HttpStatusCode.OK);
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string roleCode)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == roleCode).Id;
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

    private static void SetBearer(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static string RandomSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
