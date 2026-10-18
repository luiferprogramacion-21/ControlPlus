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
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class DocumentNumberingApiTests : IAsyncLifetime
{
    private const string AdminUser = "numbering.admin.test";
    private const string AdminPassword = "Numbering-Administrator-Test-2026!";
    private const string SupervisorUser = "numbering.supervisor.test";
    private const string SupervisorPassword = "Numbering-Supervisor-Test-2026!";

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
        {
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-numbering-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-numbering-client");
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
    public async Task DeleteContract_AnonymousIsUnauthorized_ManagerGetsMethodNotAllowed_AndOpenApiHasNoDelete()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync("/api/document-numbering/sales")).StatusCode);

        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.DeleteAsync("/api/document-numbering/sales")).StatusCode);

        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        openApiResponse.EnsureSuccessStatusCode();
        using var openApi = JsonDocument.Parse(await openApiResponse.Content.ReadAsStringAsync());
        var salesNumbering = openApi.RootElement.GetProperty("paths")
            .GetProperty("/api/document-numbering/sales");
        Assert.False(salesNumbering.TryGetProperty("delete", out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAndGet_NormalizeVta_PersistLastZero_ExposeNextOne_AndAuditSafely()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/document-numbering/sales")).StatusCode);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest(" vta ", 1)), HttpStatusCode.Created);

        Assert.Equal("VENTA", created.DocumentType);
        Assert.Equal("NUMBER1", created.Series);
        Assert.Equal("VTA", created.Prefix);
        Assert.Equal(0, created.LastNumber);
        Assert.Equal(1, created.NextNumber);
        Assert.Equal(1, created.Version);

        var getResponse = await client.GetAsync("/api/document-numbering/sales");
        Assert.Equal("application/json", getResponse.Content.Headers.ContentType?.MediaType);
        var queried = await ReadAsync<SalesDocumentNumberingDto>(getResponse, HttpStatusCode.OK);
        Assert.Equal(created, queried);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await context.ConsecutivoDocumento.AsNoTracking().SingleAsync();
        Assert.Equal(0, persisted.UltimoNumero);
        Assert.Equal("VTA", persisted.Prefijo);
        var audit = await context.EventoAuditoria.AsNoTracking().SingleAsync(x =>
            x.Accion == "SALESDOCUMENTNUMBERINGCREATED");
        var auditText = string.Join('|', audit.DatosAnteriores, audit.DatosNuevos, audit.Motivo);
        Assert.DoesNotContain(AdminPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_masterKey, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_jwtKey, auditText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("   ", 1)]
    [InlineData("ABCDEFGHIJK", 1)]
    [InlineData("VT\u0001A", 1)]
    [InlineData("VTA", -1)]
    [InlineData("VTA", 0)]
    [Trait("Category", "Integration")]
    public async Task Create_RejectsEmptyLongInvalidPrefixAndInvalidFirstNumber(string prefix, long firstNumber)
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest(prefix, firstNumber))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_AcceptsTenCharacterPrefix_AndLongMaxValueFirstNumber()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);

        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("ABCDEFGHIJ", long.MaxValue)), HttpStatusCode.Created);

        Assert.Equal("ABCDEFGHIJ", created.Prefix);
        Assert.Equal(long.MaxValue - 1, created.LastNumber);
        Assert.Equal(long.MaxValue, created.NextNumber);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_SerializesConcurrentRequests_AndRejectsDuplicate()
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        var admin = await SetupAndLoginAsync(firstClient);
        SetBearer(firstClient, admin.AccessToken);
        SetBearer(secondClient, admin.AccessToken);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/document-numbering/sales",
                new CreateSalesDocumentNumberingRequest("VTA", 1)),
            secondClient.PostAsJsonAsync("/api/document-numbering/sales",
                new CreateSalesDocumentNumberingRequest("VTA", 1)));

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, (await firstClient.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await context.ConsecutivoDocumento.CountAsync());
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x =>
            x.Accion == "SALESDOCUMENTNUMBERINGCREATED"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentUpdates_WithSameVersion_AllowExactlyOneSuccessUsingRealDatabaseWaiters()
    {
        using var setupClient = CreateClient();
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        var admin = await SetupAndLoginAsync(setupClient);
        SetBearer(setupClient, admin.AccessToken);
        SetBearer(firstClient, admin.AccessToken);
        SetBearer(secondClient, admin.AccessToken);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await setupClient.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);

        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await LockActiveInstallationAsync(blocker, blockerTransaction);

        var firstTask = firstClient.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("FIRST", 10, created.Version));
        var secondTask = secondClient.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("SECOND", 20, created.Version));
        await WaitForInstallationLockWaitersAsync(2);
        await blockerTransaction.CommitAsync();

        var responses = await Task.WhenAll(firstTask, secondTask);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var winner = await ReadAsync<SalesDocumentNumberingDto>(
            responses.Single(x => x.StatusCode == HttpStatusCode.OK), HttpStatusCode.OK);
        var persisted = await ReadAsync<SalesDocumentNumberingDto>(
            await setupClient.GetAsync("/api/document-numbering/sales"), HttpStatusCode.OK);
        Assert.Equal(winner, persisted);
        Assert.Equal(2, persisted.Version);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NumberingManage_IsRequired_AndIndividualRevocationOverridesAdministratorTemplate()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);

        var supervisorRoleId = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorUser, "Supervisor ficticio de numeración", SupervisorPassword,
                [supervisorRoleId])), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/document-numbering/sales")).StatusCode);

        SetBearer(client, admin.AccessToken);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);
        var users = await ReadAsync<PagedResult<UserDto>>(
            await client.GetAsync("/api/users?page=1&pageSize=100"), HttpStatusCode.OK);
        var administratorId = users.Items.Single(x => x.UserName == AdminUser).Id;
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.NumberingManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{administratorId}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);

        var revokedAdmin = await LoginAsync(client, AdminUser, AdminPassword);
        SetBearer(client, revokedAdmin.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/document-numbering/sales")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("OTHER", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("OTHER", 2, created.Version))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_BeforeSalesCorrectsPrefixAndFirstNumber_AndRejectsStaleVersion()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);

        var updated = await ReadAsync<SalesDocumentNumberingDto>(await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest(" alt ", 10, created.Version)), HttpStatusCode.OK);
        Assert.Equal("ALT", updated.Prefix);
        Assert.Equal(9, updated.LastNumber);
        Assert.Equal(10, updated.NextNumber);
        Assert.Equal(2, updated.Version);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("VTA", 1, created.Version))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var updateAudit = await context.EventoAuditoria.AsNoTracking().SingleAsync(x =>
            x.Accion == "SALESDOCUMENTNUMBERINGUPDATED");
        var auditText = string.Join('|', updateAudit.DatosAnteriores, updateAudit.DatosNuevos, updateAudit.Motivo);
        Assert.DoesNotContain(AdminPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_masterKey, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(_jwtKey, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("InstalacionId", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UltimoNumero", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Version", auditText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_AcceptsExactLimits_AndRejectsLongNegativeAndZeroWithoutWrites()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);

        var boundary = await ReadAsync<SalesDocumentNumberingDto>(await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("ABCDEFGHIJ", long.MaxValue, created.Version)),
            HttpStatusCode.OK);
        Assert.Equal("ABCDEFGHIJ", boundary.Prefix);
        Assert.Equal(long.MaxValue - 1, boundary.LastNumber);
        Assert.Equal(long.MaxValue, boundary.NextNumber);

        var before = await CaptureNumberingStateAsync();
        var invalid = new[]
        {
            new UpdateSalesDocumentNumberingRequest("ABCDEFGHIJK", 1, boundary.Version),
            new UpdateSalesDocumentNumberingRequest("VTA", -1, boundary.Version),
            new UpdateSalesDocumentNumberingRequest("VTA", 0, boundary.Version)
        };
        foreach (var request in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PutAsJsonAsync("/api/document-numbering/sales", request)).StatusCode);
            Assert.Equal(before, await CaptureNumberingStateAsync());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StrictJson_RejectsServerOwnedFieldsForCreateAndUpdate_WithProblemDetailsAndZeroWrites()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        var createBefore = await CaptureNumberingStateAsync();
        var forbiddenCreateFields = new Dictionary<string, object?>
        {
            ["installationId"] = Guid.CreateVersion7(),
            ["series"] = "CLIENT-SERIES",
            ["documentType"] = "COMPRA",
            ["lastNumber"] = 99L,
            ["version"] = 99L
        };
        foreach (var field in forbiddenCreateFields)
        {
            var payload = new Dictionary<string, object?>
            {
                ["prefix"] = "VTA",
                ["firstNumber"] = 1L,
                [field.Key] = field.Value
            };
            await AssertStrictJsonRejectionAsync(
                await SendRawJsonAsync(client, HttpMethod.Post, payload), field.Key);
            Assert.Equal(createBefore, await CaptureNumberingStateAsync());
        }

        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);
        var updateBefore = await CaptureNumberingStateAsync();
        var forbiddenUpdateFields = new Dictionary<string, object?>
        {
            ["installationId"] = Guid.CreateVersion7(),
            ["series"] = "CLIENT-SERIES",
            ["documentType"] = "COMPRA",
            ["lastNumber"] = 99L,
            ["persistedVersion"] = 99L
        };
        foreach (var field in forbiddenUpdateFields)
        {
            var payload = new Dictionary<string, object?>
            {
                ["prefix"] = "OTHER",
                ["firstNumber"] = 2L,
                ["version"] = created.Version,
                [field.Key] = field.Value
            };
            await AssertStrictJsonRejectionAsync(
                await SendRawJsonAsync(client, HttpMethod.Put, payload), field.Key);
            Assert.Equal(updateBefore, await CaptureNumberingStateAsync());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_DeferredCommitFailure_RollsBackNumberVersionAndAuditCompletely()
    {
        using var client = CreateClient();
        var admin = await SetupAndLoginAsync(client);
        SetBearer(client, admin.AccessToken);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);
        var before = await CaptureNumberingStateAsync();
        await InstallDeferredNumberingUpdateFailureAsync();

        var response = await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("ROLLBACK", 50, created.Version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await DeferredNumberingUpdateFailureObservedAsync());
        Assert.Equal(before, await CaptureNumberingStateAsync());
    }

    private async Task<NumberingPersistenceState> CaptureNumberingStateAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var numbering = await context.ConsecutivoDocumento.AsNoTracking().SingleOrDefaultAsync();
        return new NumberingPersistenceState(
            numbering?.Prefijo,
            numbering?.UltimoNumero,
            numbering?.Version,
            await context.ConsecutivoDocumento.CountAsync(),
            await context.EventoAuditoria.CountAsync(),
            await context.EventoAuditoria.CountAsync(x =>
                x.Accion == "SALESDOCUMENTNUMBERINGUPDATED"));
    }

    private static async Task LockActiveInstallationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM configuracion.instalacion
            WHERE activo
            ORDER BY fecha_instalacion, id
            LIMIT 1
            FOR UPDATE
            """, connection, transaction);
        Assert.NotNull(await command.ExecuteScalarAsync());
    }

    private async Task WaitForInstallationLockWaitersAsync(int expectedCount)
    {
        await using var observer = new NpgsqlConnection(_postgres.GetConnectionString());
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM configuracion.instalacion AS i%'
                  AND query LIKE '%FOR UPDATE%'
                """, observer);
            if ((long)(await command.ExecuteScalarAsync())! >= expectedCount) return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"No se observaron {expectedCount} conexiones esperando el bloqueo de instalación.");
    }

    private static async Task<HttpResponseMessage> SendRawJsonAsync(
        HttpClient client,
        HttpMethod method,
        IReadOnlyDictionary<string, object?> payload)
    {
        using var request = new HttpRequestMessage(method, "/api/document-numbering/sales")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }

    private static async Task AssertStrictJsonRejectionAsync(
        HttpResponseMessage response,
        string propertyName)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(propertyName, body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallDeferredNumberingUpdateFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE auditoria.test_numbering_update_observed;
            CREATE FUNCTION auditoria.test_fail_numbering_update_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.accion = 'SALESDOCUMENTNUMBERINGUPDATED'
                 AND EXISTS (
                   SELECT 1
                   FROM configuracion.consecutivo_documento
                   WHERE tipo_documento = 'VENTA'
                     AND prefijo = 'ROLLBACK'
                     AND ultimo_numero = 49
                     AND version = 2
                 )
              THEN
                PERFORM nextval('auditoria.test_numbering_update_observed');
                RAISE EXCEPTION 'Forced isolated numbering update failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_numbering_update_commit
              AFTER INSERT ON auditoria.evento_auditoria DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION auditoria.test_fail_numbering_update_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredNumberingUpdateFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM auditoria.test_numbering_update_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
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
                "Comercio ficticio de numeración", "NUMBERING-TEST", "NUMBERING-INSTALLATION", "NUMBER1",
                "NUMBERING-TERMINAL", "Terminal ficticia de numeración", AdminUser,
                "Administrador ficticio de numeración", AdminPassword))
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        return await LoginAsync(client, AdminUser, AdminPassword);
    }

    private static async Task<AuthenticationResult> LoginAsync(
        HttpClient client,
        string userName,
        string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(userName, password)), HttpStatusCode.OK);
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

    private static void SetBearer(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static string RandomSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record NumberingPersistenceState(
        string? Prefix,
        long? LastNumber,
        long? Version,
        int NumberingRows,
        int AuditRows,
        int UpdateAuditRows);
}
