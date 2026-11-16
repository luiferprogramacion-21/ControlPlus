using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Backups;
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

public sealed class BackupApiTests : IAsyncLifetime
{
    private const string AdminName = "backup.api.admin.test";
    private const string AdminPassword = "Backup-Api-Admin-Test-2026!";
    private const string SupervisorName = "backup.api.supervisor.test";
    private const string SupervisorPassword = "Backup-Api-Supervisor-Test-2026!";
    private const string CashierName = "backup.api.cashier.test";
    private const string CashierPassword = "Backup-Api-Cashier-Test-2026!";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly LocalPostgresDatabase _postgres = new();
    private WebApplicationFactory<Program>? _factory;
    private readonly FakeRuntime _runtime = new();
    private readonly FakeEngine _engine = new();

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
            builder.UseSetting("Jwt:Issuer", "controlplus-backup-api-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-backup-api-client");
            builder.UseSetting("Jwt:SigningKey", _jwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IBackupRuntime>(_runtime);
                services.AddSingleton<IBackupEngine>(_engine);
                // Las pruebas llaman al servicio directamente; el respaldo en segundo plano no debe competir con ellas.
                var worker = services.Single(x => x.ImplementationType == typeof(ControlPlus.Api.Backups.BackupWorker));
                services.Remove(worker);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        _engine.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Run_RecordsTheExecutionAndOneVerifiedCopyPerActiveDestination_AndTheStatusShowsThem()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        var initial = await ReadAsync<BackupStatusDto>(await client.GetAsync("/api/backups/status"), HttpStatusCode.OK);
        Assert.True(initial.ServiceEnabled);
        Assert.Equal(30, initial.IntervalMinutes);
        Assert.Null(initial.LastBackup);
        Assert.All(initial.Destinations, x => Assert.Null(x.Path));

        var local = await ReadAsync<BackupDestinationDto>(await client.PutAsJsonAsync("/api/backups/destinations/LOCAL_FISICO_SEPARADO",
            new UpdateBackupDestinationRequest(_engine.Folder("local"), true, null)), HttpStatusCode.OK);
        Assert.Equal((7, 4, 12), (local.DailyRetention, local.WeeklyRetention, local.MonthlyRetention));
        Assert.False(local.Encrypted);
        var external = await ReadAsync<BackupDestinationDto>(await client.PutAsJsonAsync("/api/backups/destinations/EXTERNO_CIFRADO",
            new UpdateBackupDestinationRequest(_engine.Folder("nube"), true, null)), HttpStatusCode.OK);
        Assert.True(external.Encrypted);

        var execution = await RunAsync(BackupCodes.Manual);
        Assert.Equal(BackupCodes.Succeeded, execution!.Result);
        Assert.True(execution.Verified);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var stored = await db.EjecucionRespaldo.AsNoTracking().SingleAsync();
            Assert.Equal(("LOGICO_PG_DUMP", "MANUAL", "EXITOSO"), (stored.Estrategia, stored.Disparador, stored.Resultado));
            Assert.Equal(_engine.Size, stored.TamanoBytes);
            var copies = await db.CopiaRespaldo.AsNoTracking().Include(x => x.DestinoRespaldo).ToArrayAsync();
            Assert.Equal(2, copies.Length);
            Assert.All(copies, x => Assert.True(x.Estado == "COMPLETADA" && x.HashVerificado && x.UbicacionFinal is not null));
            Assert.True(copies.Single(x => x.DestinoRespaldo.TipoDestino == "EXTERNO_CIFRADO").Cifrado);
            var destination = await db.DestinoRespaldo.AsNoTracking().SingleAsync(x => x.TipoDestino == "EXTERNO_CIFRADO");
            Assert.Equal("Backup:EncryptionPassphrase", destination.ReferenciaSecreto);
        }

        var status = await ReadAsync<BackupStatusDto>(await client.GetAsync("/api/backups/status"), HttpStatusCode.OK);
        Assert.Equal(execution.Id, status.LastSuccessfulBackup!.Id);
        Assert.Equal("COMPLETADA", status.LastExternalCopy!.State);
        Assert.Contains(status.RecentEvents, x => x.Description == "Envío de copia externa" && x.Result == "COMPLETADA");
        Assert.Contains(status.RecentEvents, x => x.Description == "Respaldo manual" && x.Result == "EXITOSO");
    }

    [Fact, Trait("Category", "Integration")]
    public async Task FailedDump_IsRecordedWithASanitizedMessage_AndCreatesNoCopies()
    {
        using var client = CreateClient();
        var (admin, _, _) = await BootstrapAsync(client);
        SetBearer(client, admin);
        _ = await ReadAsync<BackupDestinationDto>(await client.PutAsJsonAsync("/api/backups/destinations/LOCAL_FISICO_SEPARADO",
            new UpdateBackupDestinationRequest(_engine.Folder("local"), true, null)), HttpStatusCode.OK);
        _engine.FailDumpWith = new IOException("C:/ruta/interna/secreta no disponible");

        var execution = await RunAsync(BackupCodes.Scheduled);

        Assert.Equal(BackupCodes.Failed, execution!.Result);
        Assert.Equal("No se pudo generar el respaldo.", execution.Error);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await db.CopiaRespaldo.CountAsync());
        Assert.DoesNotContain("secreta", (await db.EjecucionRespaldo.AsNoTracking().SingleAsync()).ErrorSanitizado);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task InvalidDestinations_StaleVersions_AndOtherRoles_AreRejected()
    {
        using var client = CreateClient();
        var (admin, supervisor, cashier) = await BootstrapAsync(client);
        SetBearer(client, admin);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/backups/destinations/OTRO",
            new UpdateBackupDestinationRequest(_engine.Folder("x"), true, null)), HttpStatusCode.BadRequest, "validation.failed");
        _engine.RejectWith = "La copia local debe estar en un disco distinto al de la base de datos.";
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/backups/destinations/LOCAL_FISICO_SEPARADO",
            new UpdateBackupDestinationRequest("C:/Respaldos", true, null)), HttpStatusCode.BadRequest, "validation.failed");
        _engine.RejectWith = null;
        var created = await ReadAsync<BackupDestinationDto>(await client.PutAsJsonAsync("/api/backups/destinations/LOCAL_FISICO_SEPARADO",
            new UpdateBackupDestinationRequest(_engine.Folder("local"), true, null)), HttpStatusCode.OK);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/backups/destinations/LOCAL_FISICO_SEPARADO",
            new UpdateBackupDestinationRequest(_engine.Folder("otra"), true, created.Version!.Value + 5)), HttpStatusCode.Conflict, "concurrency.conflict");

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/backups/run", null)).StatusCode);

        foreach (var token in new[] { supervisor, cashier })
        {
            SetBearer(client, token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/backups/status")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/backups/run", null)).StatusCode);
        }
    }

    private async Task<BackupExecutionDto?> RunAsync(string trigger)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBackupService>().RunAsync(trigger);
    }

    private sealed class FakeRuntime : IBackupRuntime
    {
        public BackupRuntimeSettings Settings { get; } = new(true, 30, true, "C:/");
    }

    /// <summary>Motor de prueba: escribe archivos pequeños en una carpeta temporal en lugar de llamar a pg_dump.</summary>
    private sealed class FakeEngine : IBackupEngine, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "controlplus-backup-tests-" + Guid.NewGuid().ToString("N"));
        public long Size { get; } = 128;
        public Exception? FailDumpWith { get; set; }
        public string? RejectWith { get; set; }
        public string Folder(string name) => Path.Combine(_root, name);

        public Task<DumpResult> DumpAsync(string fileName, CancellationToken cancellationToken)
        {
            if (FailDumpWith is not null) throw FailDumpWith;
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, fileName);
            File.WriteAllBytes(path, new byte[Size]);
            return Task.FromResult(new DumpResult(path, Size, new string('A', 64), true));
        }

        public Task<CopyResult> CopyAsync(DumpResult dump, string destinationRoot, bool encrypt, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destinationRoot);
            var final = Path.Combine(destinationRoot, Path.GetFileName(dump.StagingPath) + (encrypt ? ".cpbk" : ""));
            File.Copy(dump.StagingPath, final, true);
            return Task.FromResult(new CopyResult(final, true));
        }

        public IReadOnlyList<RetainedCopy> ListCopies(string destinationRoot) => [];
        public void Delete(string destinationRoot, string fileName) { }
        public (long? FreeBytes, long? TotalBytes) GetSpace(string path) => (1_000_000, 2_000_000);
        public string? ValidateDestination(string type, string path) => RejectWith;
        public void PruneStaging(int keep) { }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(string Admin, string Supervisor, string Cashier)> BootstrapAsync(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de respaldos", "BACKUP-API-TEST", "BACKUP-API-INSTALLATION",
                   "BKP1", "BACKUP-API-TERMINAL", "Terminal ficticia de respaldos",
                   AdminName, "Administrador ficticio de respaldos", AdminPassword)) })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin);
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de respaldos",
                SupervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de respaldos",
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
