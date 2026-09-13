using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SecurityApiFlowTests : IAsyncLifetime
{
    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtSigningKey = RandomSecret(64);
    private readonly PostgreSqlContainer _postgres;
    private WebApplicationFactory<Program>? _factory;

    public SecurityApiFlowTests()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("controlplus_api_test")
            .WithUsername("controlplus_test")
            .WithPassword(RandomSecret(32))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ControlPlusDbContext(options);
        await context.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-integration-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-integration-client");
            builder.UseSetting("Jwt:SigningKey", _jwtSigningKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task InstallationLoginLockoutAuthorizationAndAudit_WorkEndToEnd()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var setup = new SetupFirstAdministratorRequest(
            "Baratísimo Integración", "TEST-ONLY", "TEST-INSTALLATION", "TESTSERIAL",
            "TEST-TERMINAL", "Terminal de integración", "admin.test", "Administrador de prueba", "Valid-Test-Password-2026!");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/auth/setup", setup)).StatusCode);

        using var setupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(setup)
        };
        setupRequest.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        var setupResponse = await client.SendAsync(setupRequest);
        Assert.Equal(HttpStatusCode.Created, setupResponse.StatusCode);

        using var repeatedSetup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(setup)
        };
        repeatedSetup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(repeatedSetup)).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin.test", "Valid-Test-Password-2026!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authentication = await login.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(authentication);
        Assert.False(string.IsNullOrWhiteSpace(authentication.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/permissions")).StatusCode);

        var cashierRoleId = await GetRoleIdAsync(client, "CAJERO");
        var createUser = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "cashier.test", "Cajero de prueba", "Valid-Cashier-Password-2026!", [cashierRoleId]));
        Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);
        var cashier = await createUser.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(cashier);

        var supervisorRoleId = await GetRoleIdAsync(client, "SUPERVISOR");
        var replaceRole = await client.PostAsJsonAsync(
            $"/api/users/{cashier.Id}/roles",
            new AssignRoleRequest(supervisorRoleId));
        Assert.Equal(HttpStatusCode.OK, replaceRole.StatusCode);
        var reassignedCashier = await replaceRole.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(reassignedCashier);
        Assert.Collection(reassignedCashier.Roles, role => Assert.Equal("SUPERVISOR", role.Code));

        client.DefaultRequestHeaders.Authorization = null;
        var cashierLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("cashier.test", "Valid-Cashier-Password-2026!"));
        Assert.Equal(HttpStatusCode.OK, cashierLogin.StatusCode);
        var cashierAuthentication = await cashierLogin.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(cashierAuthentication);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierAuthentication.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);

        var usersReadPermissionId = await GetPermissionIdAsync(client, authentication.AccessToken, "USERS.READ");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync($"/api/roles/{supervisorRoleId}/permissions/{usersReadPermissionId}", null)).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierAuthentication.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var loginAfterGrant = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("cashier.test", "Valid-Cashier-Password-2026!"));
        Assert.Equal(HttpStatusCode.OK, loginAfterGrant.StatusCode);
        var authenticationAfterGrant = await loginAfterGrant.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(authenticationAfterGrant);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authenticationAfterGrant.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.DeleteAsync($"/api/roles/{supervisorRoleId}/permissions/{usersReadPermissionId}")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authenticationAfterGrant.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var loginAfterRevoke = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("cashier.test", "Valid-Cashier-Password-2026!"));
        Assert.Equal(HttpStatusCode.OK, loginAfterRevoke.StatusCode);
        var authenticationAfterRevoke = await loginAfterRevoke.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(authenticationAfterRevoke);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authenticationAfterRevoke.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("cashier.test", "incorrect-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }
        var locked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("cashier.test", "Valid-Cashier-Password-2026!"));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        var audit = await client.GetAsync("/api/audit-records?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var officialContext = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var actions = await officialContext.EventoAuditoria
            .AsNoTracking()
            .Select(record => record.Accion)
            .ToArrayAsync();
        Assert.Contains("LOGINFAILED", actions);
        Assert.Contains("USERLOCKED", actions);
        Assert.Contains("PERMISSIONGRANTED", actions);
        Assert.Contains("PERMISSIONREVOKED", actions);
    }

    [Fact]
    public async Task SetupWithRawJsonStringFields_IsAccepted()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = new StringContent(
                """
                {
                  "establishmentName": "Comercio ficticio",
                  "establishmentIdentification": "900000000-1",
                  "installationCode": "INSTALL-TEST",
                  "installationSerial": "SER0000001",
                  "terminalCode": "TERM-TEST",
                  "terminalName": "Terminal ficticia",
                  "userName": "admin.raw.test",
                  "displayName": "Administrador ficticio",
                  "password": "isolated-test-password"
                }
                """,
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SetupWithNumericIdentification_ReturnsFieldValidationProblemDetails()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = new StringContent(
                """
                {
                  "establishmentName": "Comercio ficticio",
                  "establishmentIdentification": 900000001,
                  "installationCode": "INSTALL-TEST",
                  "installationSerial": "SER0000001",
                  "terminalCode": "TERM-TEST",
                  "terminalName": "Terminal ficticia",
                  "userName": "admin.raw.test",
                  "displayName": "Administrador ficticio",
                  "password": "isolated-test-password"
                }
                """,
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);

        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(responseBody);
        var errors = json.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("$.establishmentIdentification", out _), responseBody);
    }

    [Fact]
    public async Task InitialAdministratorRecovery_WithInvalidMasterKey_IsForbidden()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = RecoveryRequest("invalid-test-master-key", "Recovery-Test-Password-2026!");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InitialAdministratorRecovery_WhenAdministratorIsAvailable_IsRejected()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SetupInitialAdministratorAsync(client, "Initial-Test-Password-2026!");
        using var request = RecoveryRequest(_masterKey, "Recovery-Test-Password-2026!");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InitialAdministratorRecovery_WhenLocked_ResetsPasswordAndInvalidatesSessions()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string originalPassword = "Initial-Test-Password-2026!";
        const string recoveredPassword = "Recovery-Test-Password-2026!";
        await SetupInitialAdministratorAsync(client, originalPassword);

        var initialLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("initial.admin.test", originalPassword));
        var initialAuthentication = await initialLogin.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.Equal(HttpStatusCode.OK, initialLogin.StatusCode);
        Assert.NotNull(initialAuthentication);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("initial.admin.test", "Incorrect-Test-Password"));
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        string stampBeforeRecovery;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            stampBeforeRecovery = await context.Usuario
                .AsNoTracking()
                .Where(user => user.NombreUsuarioNormalizado == "INITIAL.ADMIN.TEST")
                .Select(user => user.SelloSeguridad)
                .SingleAsync();
        }

        using var request = RecoveryRequest(_masterKey, recoveredPassword);
        var recovery = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, recovery.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var recovered = await context.Usuario
                .AsNoTracking()
                .SingleAsync(user => user.NombreUsuarioNormalizado == "INITIAL.ADMIN.TEST");
            Assert.True(recovered.Activo);
            Assert.Equal(0, recovered.IntentosFallidos);
            Assert.Null(recovered.BloqueoHasta);
            Assert.NotEqual(stampBeforeRecovery, recovered.SelloSeguridad);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            initialAuthentication.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("initial.admin.test", originalPassword))).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("initial.admin.test", recoveredPassword))).StatusCode);
    }

    [Fact]
    public async Task InitialAdministratorRecovery_WritesExplicitAuditEvent()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SetupInitialAdministratorAsync(client, "Initial-Test-Password-2026!");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest("initial.admin.test", "Incorrect-Test-Password"));
        }

        using var request = RecoveryRequest(_masterKey, "Recovery-Test-Password-2026!");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(request)).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var audit = await context.EventoAuditoria
            .AsNoTracking()
            .SingleAsync(record => record.Accion == "INITIALADMINISTRATORRECOVERED");
        Assert.Equal("EXITOSO", audit.Resultado);
        Assert.Equal("User", audit.Entidad);
        Assert.Null(audit.UsuarioId);
        using var details = JsonDocument.Parse(audit.DatosNuevos!);
        Assert.Equal(
            "installation_master_key",
            details.RootElement.GetProperty("RecoveryMethod").GetString());
        Assert.True(details.RootElement.GetProperty("SessionsInvalidated").GetBoolean());
        Assert.DoesNotContain("PasswordHash", audit.DatosNuevos, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NewPassword", audit.DatosNuevos, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HybridPermissions_ApplyIndividualPrecedenceAndResetToRole()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SetupInitialAdministratorAsync(client, "Initial-Test-Password-2026!");
        var administrator = await LoginAsync(client, "initial.admin.test", "Initial-Test-Password-2026!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);

        var cashierRoleId = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var created = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "hybrid.cashier.test", "Cajero híbrido", "Cashier-Test-Password-2026!", [cashierRoleId]));
        var cashier = await created.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(cashier);

        var cashierSession = await LoginAsync(client, "hybrid.cashier.test", "Cashier-Test-Password-2026!");
        var productsReadId = await GetPermissionIdAsync(client, administrator.AccessToken, PermissionCodes.ProductsRead);
        var costsReadId = await GetPermissionIdAsync(client, administrator.AccessToken, PermissionCodes.ProductCostsRead);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{cashier.Id}/permissions/{costsReadId}", new SetUserPermissionRequest(true))).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierSession.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{cashier.Id}/permissions/{costsReadId}", new SetUserPermissionRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{cashier.Id}/permissions/{costsReadId}", new SetUserPermissionRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{cashier.Id}/permissions/{productsReadId}", new SetUserPermissionRequest(false))).StatusCode);

        var effective = await GetEffectivePermissionsAsync(client, cashier.Id);
        AssertPermission(effective, PermissionCodes.ProductCostsRead, true, "INDIVIDUAL", "CONCEDER");
        AssertPermission(effective, PermissionCodes.ProductsRead, false, "INDIVIDUAL", "REVOCAR");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/users/{cashier.Id}/permissions")).StatusCode);
        effective = await GetEffectivePermissionsAsync(client, cashier.Id);
        AssertPermission(effective, PermissionCodes.ProductCostsRead, false, "ROL", null);
        AssertPermission(effective, PermissionCodes.ProductsRead, true, "ROL", null);
    }

    [Fact]
    public async Task RoleTemplate_CanBeRestoredAndLastSecurityAdministratorIsProtected()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SetupInitialAdministratorAsync(client, "Initial-Test-Password-2026!");
        var administrator = await LoginAsync(client, "initial.admin.test", "Initial-Test-Password-2026!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);

        var cashierRoleId = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var productsReadId = await GetPermissionIdAsync(client, administrator.AccessToken, PermissionCodes.ProductsRead);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
            $"/api/roles/{cashierRoleId}/permissions/{productsReadId}")).StatusCode);

        var reset = await client.PostAsync($"/api/roles/{cashierRoleId}/permissions/reset", null);
        var restoredRole = await reset.Content.ReadFromJsonAsync<RoleDto>();
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.NotNull(restoredRole);
        Assert.Contains(restoredRole.Permissions, x => x.Code == PermissionCodes.ProductsRead);

        var administratorRoleId = await GetRoleIdAsync(client, RoleCodes.Administrator);
        var usersManageId = await GetPermissionIdAsync(client, administrator.AccessToken, PermissionCodes.UsersManage);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(
            $"/api/roles/{administratorRoleId}/permissions/{usersManageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync(
            $"/api/permissions/{usersManageId}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync(
            $"/api/users/{administrator.User.Id}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Supervisor_CanResetCashierPasswordButNotAnotherSupervisorPassword()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SetupInitialAdministratorAsync(client, "Initial-Test-Password-2026!");
        var administrator = await LoginAsync(client, "initial.admin.test", "Initial-Test-Password-2026!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);

        var supervisorRoleId = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRoleId = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var supervisorResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "password.supervisor.test", "Supervisor ficticio", "Supervisor-Test-Password-2026!", [supervisorRoleId]));
        var supervisor = await supervisorResponse.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(HttpStatusCode.Created, supervisorResponse.StatusCode);
        Assert.NotNull(supervisor);

        var otherSupervisorResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "password.supervisor.two.test", "Segundo supervisor ficticio", "Supervisor-Two-Test-Password-2026!", [supervisorRoleId]));
        var otherSupervisor = await otherSupervisorResponse.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(HttpStatusCode.Created, otherSupervisorResponse.StatusCode);
        Assert.NotNull(otherSupervisor);

        var cashierResponse = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "password.cashier.test", "Cajero ficticio", "Cashier-Old-Test-Password-2026!", [cashierRoleId]));
        var cashier = await cashierResponse.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(HttpStatusCode.Created, cashierResponse.StatusCode);
        Assert.NotNull(cashier);

        var supervisorSession = await LoginAsync(client, supervisor.UserName, "Supervisor-Test-Password-2026!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisorSession.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{cashier.Id}/password",
            new ChangePasswordRequest(string.Empty, "Cashier-New-Test-Password-2026!"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
            $"/api/users/{otherSupervisor.Id}/password",
            new ChangePasswordRequest(string.Empty, "Forbidden-Test-Password-2026!"))).StatusCode);

        var cashierSession = await LoginAsync(client, cashier.UserName, "Cashier-New-Test-Password-2026!");
        Assert.Equal(cashier.Id, cashierSession.User.Id);
    }

    private async Task SetupInitialAdministratorAsync(HttpClient client, string password)
    {
        var setup = new SetupFirstAdministratorRequest(
            "Comercio ficticio",
            "TEST-ONLY",
            "TEST-INSTALLATION",
            "TESTSERIAL",
            "TEST-TERMINAL",
            "Terminal ficticia",
            "initial.admin.test",
            "Administrador ficticio",
            password);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(setup)
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
    }

    private static HttpRequestMessage RecoveryRequest(string masterKey, string newPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/recover-initial-administrator")
        {
            Content = JsonContent.Create(new RecoverInitialAdministratorRequest("initial.admin.test", newPassword))
        };
        request.Headers.Add("X-ControlPlus-Master-Key", masterKey);
        return request;
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticationResult>())!;
    }

    private static async Task<IReadOnlyCollection<EffectiveUserPermissionDto>> GetEffectivePermissionsAsync(
        HttpClient client,
        Guid userId)
    {
        var response = await client.GetAsync($"/api/users/{userId}/permissions/effective");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EffectiveUserPermissionDto[]>())!;
    }

    private static void AssertPermission(
        IReadOnlyCollection<EffectiveUserPermissionDto> permissions,
        string code,
        bool granted,
        string source,
        string? effect)
    {
        var permission = Assert.Single(permissions, x => x.Code == code);
        Assert.Equal(granted, permission.Granted);
        Assert.Equal(source, permission.Source);
        Assert.Equal(effect, permission.IndividualEffect);
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var response = await client.GetAsync("/api/roles?page=1&pageSize=20");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(role => role.GetProperty("code").GetString() == code);
        return item.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string administratorToken, string code)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administratorToken);
        var response = await client.GetAsync("/api/permissions?page=1&pageSize=100");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray()
            .Single(permission => permission.GetProperty("code").GetString() == code);
        return item.GetProperty("id").GetGuid();
    }

    private static string RandomSecret(int byteCount) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));
}
