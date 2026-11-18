using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Recuperación de contraseña por correo (decisión del 18 de noviembre de 2026), con un correo simulado.</summary>
public sealed class PasswordRecoveryApiTests : IAsyncLifetime
{
    private const string AdminName = "recovery.admin.test";
    private const string CashierName = "recovery.cashier.test";
    private const string CashierEmail = "cajera.recuperacion@example.com";
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly string _masterKey = Secret(48);
    private readonly string _adminPassword = "Admin-" + Secret(12);
    private readonly string _cashierPassword = "Cashier-" + Secret(12);
    private readonly FakeEmailSender _email = new();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-recovery-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-recovery-client");
            builder.UseSetting("Jwt:SigningKey", Secret(64));
            builder.UseSetting("Jwt:AccessTokenMinutes", "30");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_email);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CodeByEmail_ChangesThePassword_ClosesSessions_AndCannotBeReused()
    {
        using var client = Client();
        var cashierId = await BootstrapAsync(client);
        var oldSession = await LoginAsync(client, CashierName, _cashierPassword);

        // Usuario inexistente y usuario sin correo: misma respuesta y ningún correo.
        var unknown = await RequestAsync(client, "no.existe.test");
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        var generic = (await unknown.Content.ReadFromJsonAsync<PasswordRecoveryRequestedDto>())!.Message;
        Assert.Contains("vale 10 minutos", generic);
        var withoutEmail = await RequestAsync(client, AdminName);
        Assert.Equal(generic, (await withoutEmail.Content.ReadFromJsonAsync<PasswordRecoveryRequestedDto>())!.Message);
        Assert.Empty(_email.Sent);

        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync(client, CashierName.ToUpperInvariant())).StatusCode);
        var mail = Assert.Single(_email.Sent);
        Assert.Equal(CashierEmail, mail.To);
        Assert.DoesNotContain(_cashierPassword, mail.Body);
        var code = Regex.Match(mail.Body, @"\b\d{6}\b").Value;
        var wrong = code == "000000" ? "111111" : "000000";

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(client, CashierName, wrong, "Nueva-" + Secret(8))).StatusCode);
        Assert.Equal(1, await FailedAttemptsAsync(cashierId));

        var newPassword = "Nueva-" + Secret(12);
        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(client, CashierName, code, newPassword)).StatusCode);
        Assert.Equal(0, await FailedAttemptsAsync(cashierId));
        Assert.Equal(cashierId, (await LoginAsync(client, CashierName, newPassword)).User.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(CashierName, _cashierPassword))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldSession.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        // El mismo código ya no sirve: el sello de seguridad cambió con la contraseña.
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(client, CashierName, code, "Otra-" + Secret(8))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var actions = await db.EventoAuditoria.AsNoTracking().Where(x => x.EntidadId == cashierId)
            .Select(x => x.Accion).ToListAsync();
        // La auditoría guarda el nombre de la acción en mayúsculas.
        Assert.Contains("PASSWORDRECOVERYREQUESTED", actions);
        Assert.Contains("PASSWORDRECOVERYFAILED", actions);
        Assert.Contains("PASSWORDRECOVERED", actions);
        // Ni el correo ni el código quedan en la auditoría (la columna es jsonb: se compara en memoria).
        var auditData = await db.EventoAuditoria.AsNoTracking().Select(x => x.DatosNuevos).ToListAsync();
        Assert.DoesNotContain(auditData, data => data != null && (data.Contains(CashierEmail) || data.Contains(code)));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task FiveWrongCodes_LockTheUser_AndThenEvenTheRightCodeIsRejected()
    {
        using var client = Client();
        var cashierId = await BootstrapAsync(client);
        // El código que se habría enviado (sin gastar una solicitud del límite por minuto).
        string code;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var stamp = await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
                .Usuario.AsNoTracking().Where(x => x.Id == cashierId).Select(x => x.SelloSeguridad).SingleAsync();
            code = scope.ServiceProvider.GetRequiredService<IPasswordRecoveryCodes>().Create(cashierId, stamp, DateTimeOffset.UtcNow);
        }
        var wrong = code == "000000" ? "111111" : "000000";
        for (var attempt = 1; attempt <= 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(client, CashierName, wrong, "Nueva-" + Secret(8))).StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
                .Usuario.AsNoTracking().SingleAsync(x => x.Id == cashierId);
            Assert.NotNull(user.BloqueoHasta);
            Assert.Equal(5, user.IntentosFallidos);
        }

        // Bloqueado: ni el código correcto sirve; lo reactiva el Administrador.
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(client, CashierName, code, "Nueva-" + Secret(8))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(CashierName, _cashierPassword))).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task WithoutEmailConfiguration_OrWhenSendingFails_TheUserIsToldToAskTheAdministrator()
    {
        using var client = Client();
        await BootstrapAsync(client);
        _email.Configured = false;
        var notConfigured = await RequestAsync(client, CashierName);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notConfigured.StatusCode);
        Assert.Contains("Administrador", await notConfigured.Content.ReadAsStringAsync());

        _email.Configured = true; _email.Fail = true;
        var failed = await RequestAsync(client, CashierName);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Contains("Internet", await failed.Content.ReadAsStringAsync());
        Assert.Empty(_email.Sent);
    }

    private HttpClient Client() => _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<Guid> BootstrapAsync(HttpClient client)
    {
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest("Comercio ficticio de recuperación", "RECOVERY-TEST",
                "RECOVERY-INSTALLATION", "RCV1", "RECOVERY-TERMINAL", "Terminal ficticia", AdminName, "Administrador ficticio",
                _adminPassword))
        };
        setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        var roles = (await client.GetFromJsonAsync<ControlPlus.Application.Common.PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        var cashier = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(CashierName, "Cajera ficticia",
            _cashierPassword, [roles.Items.Single(x => x.Code == RoleCodes.Cashier).Id], CashierEmail));
        Assert.Equal(HttpStatusCode.Created, cashier.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        return (await cashier.Content.ReadFromJsonAsync<UserDto>())!.Id;
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticationResult>())!;
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string userName) =>
        client.PostAsJsonAsync("/api/auth/password-recovery", new PasswordRecoveryRequest(userName));

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string userName, string code, string password) =>
        client.PostAsJsonAsync("/api/auth/password-recovery/confirm", new PasswordRecoveryConfirmRequest(userName, code, password));

    private async Task<int> FailedAttemptsAsync(Guid userId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
            .Usuario.AsNoTracking().Where(x => x.Id == userId).Select(x => x.IntentosFallidos).SingleAsync();
    }

    private static string Secret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));

    private sealed class FakeEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();
        public bool Configured { get; set; } = true;
        public bool Fail { get; set; }
        public bool IsConfigured => Configured;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new EmailDeliveryException("Sin conexión simulada.");
            Sent.Enqueue(message);
            return Task.CompletedTask;
        }
    }
}
