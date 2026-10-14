using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class PurchaseDocumentNumberingApiTests : IAsyncLifetime
{
    private const string Route = "/api/document-numbering/purchases";
    private const string AdminUser = "purchase.numbering.admin";
    private const string AdminPassword = "Purchase-Numbering-Administrator-2026!";
    private const string SupervisorUser = "purchase.numbering.supervisor";
    private const string SupervisorPassword = "Purchase-Numbering-Supervisor-2026!";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("controlplus_purchase_numbering_test")
        .WithUsername("controlplus_test")
        .WithPassword(RandomSecret(32))
        .Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await PostgresAvailability.WaitAsync(_postgres);
        await using (var context = new ControlPlusDbContext(
                         new DbContextOptionsBuilder<ControlPlusDbContext>()
                             .UseNpgsql(_postgres.GetConnectionString()).Options))
        {
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-purchase-numbering-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-purchase-numbering-client");
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
    public async Task PurchaseNumbering_CreatesOneCmpSequence_UpdatesOnlyWithCurrentVersion_AndAudits()
    {
        using var client = CreateClient();
        SetBearer(client, (await SetupAndLoginAsync(client)).AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Route,
                new CreatePurchaseDocumentNumberingRequest("OTHER", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Route,
                new CreatePurchaseDocumentNumberingRequest("CMP", 2))).StatusCode);

        var createdResponse = await client.PostAsJsonAsync(Route,
            new CreatePurchaseDocumentNumberingRequest(" cmp ", 1));
        Assert.Equal("application/json", createdResponse.Content.Headers.ContentType?.MediaType);
        var created = await ReadAsync<PurchaseDocumentNumberingDto>(createdResponse, HttpStatusCode.Created);
        Assert.Equal("COMPRA", created.DocumentType);
        Assert.Equal("NUMBER1", created.Series);
        Assert.Equal("CMP", created.Prefix);
        Assert.Equal(0, created.LastNumber);
        Assert.Equal(1, created.NextNumber);
        Assert.Equal(1, created.Version);
        Assert.Equal(created,
            await ReadAsync<PurchaseDocumentNumberingDto>(await client.GetAsync(Route), HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(Route,
                new CreatePurchaseDocumentNumberingRequest("CMP", 1))).StatusCode);

        var updated = await ReadAsync<PurchaseDocumentNumberingDto>(
            await client.PutAsJsonAsync(Route,
                new UpdatePurchaseDocumentNumberingRequest(" cmp ", 12, created.Version)),
            HttpStatusCode.OK);
        Assert.Equal(11, updated.LastNumber);
        Assert.Equal(12, updated.NextNumber);
        Assert.Equal(2, updated.Version);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PutAsJsonAsync(Route,
                new UpdatePurchaseDocumentNumberingRequest("CMP", 13, created.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(Route,
                new UpdatePurchaseDocumentNumberingRequest("OTHER", 13, updated.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(Route,
                new UpdatePurchaseDocumentNumberingRequest("CMP", 0, updated.Version))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await db.ConsecutivoDocumento.AsNoTracking().SingleAsync();
        Assert.Equal("COMPRA", persisted.TipoDocumento);
        Assert.Equal("CMP", persisted.Prefijo);
        Assert.Equal(11, persisted.UltimoNumero);
        Assert.Equal(2, persisted.Version);
        var audit = await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.Accion.StartsWith("PURCHASEDOCUMENTNUMBERING"))
            .OrderBy(x => x.Accion).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, x => x.Accion == "PURCHASEDOCUMENTNUMBERINGCREATED");
        Assert.Contains(audit, x => x.Accion == "PURCHASEDOCUMENTNUMBERINGUPDATED");
        var auditText = string.Join('|', audit.SelectMany(x => new[]
            { x.DatosAnteriores, x.DatosNuevos, x.Motivo }));
        Assert.DoesNotContain(AdminPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_masterKey, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_jwtKey, auditText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PurchaseNumbering_RejectsUnknownJsonAndServerOwnedFields()
    {
        using var client = CreateClient();
        SetBearer(client, (await SetupAndLoginAsync(client)).AccessToken);

        foreach (var field in new[] { "installationId", "series", "documentType", "lastNumber", "version" })
        {
            var payload = new Dictionary<string, object?>
            {
                ["prefix"] = "CMP", ["firstNumber"] = 1, [field] = "CLIENT-OWNED"
            };
            await AssertBadRequestProblemAsync(await SendJsonAsync(client, HttpMethod.Post, payload));
        }

        var created = await ReadAsync<PurchaseDocumentNumberingDto>(
            await client.PostAsJsonAsync(Route,
                new CreatePurchaseDocumentNumberingRequest("CMP", 1)),
            HttpStatusCode.Created);
        foreach (var field in new[] { "installationId", "series", "documentType", "lastNumber", "purchaseId" })
        {
            var payload = new Dictionary<string, object?>
            {
                ["prefix"] = "CMP", ["firstNumber"] = 2,
                ["version"] = created.Version, [field] = "CLIENT-OWNED"
            };
            await AssertBadRequestProblemAsync(await SendJsonAsync(client, HttpMethod.Put, payload));
        }

        Assert.Equal(created,
            await ReadAsync<PurchaseDocumentNumberingDto>(await client.GetAsync(Route), HttpStatusCode.OK));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PurchaseNumbering_RequiresEffectivePermission_AndOpenApiHasNoDelete()
    {
        using var client = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(Route)).StatusCode);

        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        var supervisorRoleId = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorUser, "Supervisor ficticio de compras",
                SupervisorPassword, [supervisorRoleId])), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Route,
                new CreatePurchaseDocumentNumberingRequest("CMP", 1))).StatusCode);

        SetBearer(client, admin.AccessToken);
        var users = await ReadAsync<PagedResult<UserDto>>(
            await client.GetAsync("/api/users?page=1&pageSize=100"), HttpStatusCode.OK);
        var supervisorId = users.Items.Single(x => x.UserName == SupervisorUser).Id;
        var adminId = users.Items.Single(x => x.UserName == AdminUser).Id;
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.NumberingManage);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/users/{supervisorId}/permissions/{permissionId}",
                new SetUserPermissionRequest(true))).StatusCode);
        supervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route)).StatusCode);

        SetBearer(client, admin.AccessToken);
        _ = await ReadAsync<PurchaseDocumentNumberingDto>(await client.PostAsJsonAsync(Route,
            new CreatePurchaseDocumentNumberingRequest("CMP", 1)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/users/{adminId}/permissions/{permissionId}",
                new SetUserPermissionRequest(false))).StatusCode);
        SetBearer(client, (await LoginAsync(client, AdminUser, AdminPassword)).AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route)).StatusCode);

        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Route)).StatusCode);

        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        openApiResponse.EnsureSuccessStatusCode();
        using var openApi = JsonDocument.Parse(await openApiResponse.Content.ReadAsStringAsync());
        var path = openApi.RootElement.GetProperty("paths").GetProperty(Route);
        foreach (var operation in new[] { "get", "post", "put" })
        {
            var endpoint = path.GetProperty(operation);
            Assert.True(endpoint.TryGetProperty("security", out _));
            var responses = endpoint.GetProperty("responses");
            var successCode = operation == "post" ? "201" : "200";
            Assert.True(responses.GetProperty(successCode).GetProperty("content")
                .TryGetProperty("application/json", out _));
            foreach (var code in new[] { "400", "401", "403", "404", "409" })
                Assert.True(responses.GetProperty(code).GetProperty("content")
                    .TryGetProperty("application/problem+json", out _));
        }
        Assert.False(path.TryGetProperty("delete", out _));
        Assert.DoesNotContain(openApi.RootElement.GetProperty("paths")
            .EnumerateObject(), x => x.Name.StartsWith(Route + "/", StringComparison.Ordinal));
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
                "Comercio ficticio de numeración de compras", "PURCHASE-NUMBERING-TEST",
                "PURCHASE-NUMBERING-INSTALLATION", "NUMBER1", "PURCHASE-NUMBERING-TERMINAL",
                "Terminal ficticia de numeración de compras", AdminUser,
                "Administrador ficticio de compras", AdminPassword))
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

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string permissionCode)
    {
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(x => x.Code == permissionCode).Id;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client, HttpMethod method, IReadOnlyDictionary<string, object?> payload)
    {
        using var request = new HttpRequestMessage(method, Route)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }

    private static async Task AssertBadRequestProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());
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
