using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>
/// Contraseñas de 4 números para todos (decisión del 19 de noviembre de 2026). Una contraseña anterior sigue sirviendo
/// para entrar, pero esa sesión solo puede cambiarla por una de 4 números.
/// </summary>
public sealed class PinPasswordApiTests : IAsyncLifetime
{
    private const string AdminName = "pin.admin.test";
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly string _masterKey = Secret(48);
    private readonly string _signingKey = Secret(64);
    private readonly string _oldPassword = "Admin-" + Secret(12);
    private WebApplicationFactory<Program>? _free;
    private WebApplicationFactory<Program>? _pin;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();
        _free = Factory(pin: false);
        _pin = Factory(pin: true);
    }

    public async Task DisposeAsync()
    {
        if (_free is not null) await _free.DisposeAsync();
        if (_pin is not null) await _pin.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OldPassword_StillLogsIn_ButTheSessionCanOnlyChangeItToFourDigits()
    {
        // La instalación existía antes de la regla: el Administrador tiene una contraseña larga.
        using (var free = _free!.CreateClient())
        {
            using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
            {
                Content = JsonContent.Create(new SetupFirstAdministratorRequest("Comercio ficticio de claves", "PIN-TEST",
                    "PIN-INSTALLATION", "PIN1", "PIN-TERMINAL", "Terminal ficticia", AdminName, "Administrador ficticio",
                    _oldPassword))
            };
            setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await free.SendAsync(setup)).StatusCode);
        }

        using var client = _pin!.CreateClient();
        var restricted = await LoginAsync(client, AdminName, _oldPassword);
        Assert.True(restricted.PasswordChangeRequired);
        SetBearer(client, restricted.AccessToken);
        var blocked = await client.GetAsync("/api/roles?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("password.change_required", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var renewed = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.True((await renewed.Content.ReadFromJsonAsync<AuthenticationResult>())!.PasswordChangeRequired);

        foreach (var invalid in new[] { "12a4", "123", "12345", _oldPassword })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/password-change",
                new ChangeRequiredPasswordRequest(_oldPassword, invalid))).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password-change",
            new ChangeRequiredPasswordRequest("otra-cosa", "4821"))).StatusCode);

        var changed = await client.PostAsJsonAsync("/api/auth/password-change", new ChangeRequiredPasswordRequest(_oldPassword, "4821"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var session = (await changed.Content.ReadFromJsonAsync<AuthenticationResult>())!;
        Assert.False(session.PasswordChangeRequired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        SetBearer(client, session.AccessToken);
        var roles = (await client.GetFromJsonAsync<ControlPlus.Application.Common.PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;

        // Con la regla, toda contraseña nueva es de 4 números.
        var cashierRole = roles.Items.Single(x => x.Code == RoleCodes.Cashier).Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest("pin.cajero.largo", "Cajera ficticia", "Cajera-2026!", [cashierRole]))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest("pin.cajero", "Cajera ficticia", "1357", [cashierRole]))).StatusCode);

        SetBearer(client, string.Empty);
        Assert.False((await LoginAsync(client, AdminName, "4821")).PasswordChangeRequired);
        Assert.False((await LoginAsync(client, "pin.cajero", "1357")).PasswordChangeRequired);
    }

    private WebApplicationFactory<Program> Factory(bool pin) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
        builder.UseSetting("Installation:MasterKey", _masterKey);
        builder.UseSetting("Jwt:Issuer", "controlplus-pin-tests");
        builder.UseSetting("Jwt:Audience", "controlplus-pin-client");
        builder.UseSetting("Jwt:SigningKey", _signingKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "30");
        builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        if (pin) builder.UseSetting("Security:CredentialFormat", "PIN4");
    });

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticationResult>())!;
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);

    private static string Secret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));
}
