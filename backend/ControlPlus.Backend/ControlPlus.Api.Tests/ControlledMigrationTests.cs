using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ControlPlus.Api.Migrations;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ControlledMigrationCollection : ICollectionFixture<ControlledMigrationPostgresFixture>
{
    public const string Name = "Controlled migration PostgreSQL 17";
}

public sealed class ControlledMigrationPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("controlled_migration_tests")
        .WithUsername("migration_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"migration_test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
    }
}

[Collection(ControlledMigrationCollection.Name)]
public sealed class ControlledMigrationTests(ControlledMigrationPostgresFixture postgres)
{
    private const string PreviousMigration = "20260913010000_SeedMeasurementUnitsV1";
    private const string CashMigration = "20260913020000_CashRegisterModuleV1";

    [Fact]
    public async Task NormalStartup_DoesNotApplyMigrations_AndKeepsHealthAndOpenApiAvailable()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", connectionString);
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlled-migration-tests");
            builder.UseSetting("Jwt:Audience", "controlled-migration-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task MissingEnablementVariable_IsRejectedWithoutDatabaseChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(connectionString, null, CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("must be exactly true", result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("false")]
    public async Task EnablementVariableNotExactlyLowercaseTrue_IsRejectedWithoutDatabaseChanges(string value)
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(connectionString, value, CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task MissingTargetArgument_IsRejectedWithoutDatabaseChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(connectionString, "true");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--migrate-to", result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task UnknownTarget_IsRejectedWithoutDatabaseChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(connectionString, "true", "20990101000000_DoesNotExist");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("does not exist", result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task AlreadyAppliedTarget_IsRejectedWithoutAdditionalHistoryEntry()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ApplyAsync(connectionString, CashMigration);

        var result = await RunApiProcessAsync(connectionString, "true", CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("already applied", result.Output, StringComparison.Ordinal);
        Assert.Equal(5L, await HistoryCountAsync(connectionString));
        Assert.Equal(1L, await MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task TargetBehindCurrentHistory_IsRejectedAsReversalWithoutChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ApplyAsync(connectionString, CashMigration);

        var result = await RunApiProcessAsync(connectionString, "true", PreviousMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("require a reversal", result.Output, StringComparison.Ordinal);
        Assert.Equal(5L, await HistoryCountAsync(connectionString));
        Assert.Equal(1L, await MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task UnexpectedHistoryEntry_IsRejectedAsIncoherentWithoutApplyingAnythingElse()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ApplyAsync(connectionString, PreviousMigration);
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20990101000000_UnexpectedMigration', '10.0.3')
            """);

        var result = await RunApiProcessAsync(connectionString, "true", CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not a coherent catalog prefix", result.Output, StringComparison.Ordinal);
        Assert.Equal(5L, await HistoryCountAsync(connectionString));
        Assert.Equal(0L, await MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task ExactPendingCashTarget_IsAppliedOnce_AndMigrationModeNeverListensForHttp()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(connectionString, "true", CashMigration);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"'{CashMigration}' applied successfully", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(5L, await HistoryCountAsync(connectionString));
        Assert.Equal(1L, await MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task ProviderFailure_ReturnsNonZeroWithoutRevealingConnectionSecret()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        const string sentinel = "DO-NOT-LEAK-THIS-PASSWORD";
        var invalidConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = sentinel
        }.ConnectionString;

        var result = await RunApiProcessAsync(invalidConnection, "true", CashMigration);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"Migration target '{CashMigration}' failed", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidConnection, result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    private static async Task ApplyAsync(string connectionString, string target)
    {
        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var context = new ControlPlusDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> HistoryCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var existence = new NpgsqlCommand(
                         "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL",
                         connection))
        {
            if (!Convert.ToBoolean(await existence.ExecuteScalarAsync()))
            {
                return 0L;
            }
        }

        await using var count = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\"", connection);
        return Convert.ToInt64(await count.ExecuteScalarAsync());
    }

    private static async Task<long> MigrationCountAsync(string connectionString, string migration)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @migration",
            connection);
        command.Parameters.AddWithValue("migration", migration);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    internal static async Task<MigrationProcessResult> RunApiProcessAsync(
        string? connectionString,
        string? enabledValue,
        string? target = null,
        bool includeHealthCheck = false)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var port = ReserveTcpPort();
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        if (includeHealthCheck)
        {
            startInfo.ArgumentList.Add("--health-check");
        }

        if (target is not null)
        {
            startInfo.ArgumentList.Add("--migrate-to");
            startInfo.ArgumentList.Add(target);
        }

        startInfo.Environment.Remove("ConnectionStrings__ControlPlusDb");
        if (connectionString is not null)
        {
            startInfo.Environment["ConnectionStrings__ControlPlusDb"] = connectionString;
        }
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment.Remove("CONTROLPLUS_MIGRATIONS_ENABLED");
        if (enabledValue is not null)
        {
            startInfo.Environment["CONTROLPLUS_MIGRATIONS_ENABLED"] = enabledValue;
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var portObserver = ObservePortUntilExitAsync(process, port);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The migration process did not exit within two minutes.");
        }

        var output = string.Concat(await standardOutput, await standardError);
        return new MigrationProcessResult(process.ExitCode, output, await portObserver);
    }

    private static async Task<bool> ObservePortUntilExitAsync(Process process, int port)
    {
        while (!process.HasExited)
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                return true;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                await Task.Delay(20);
            }
        }

        return false;
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal sealed record MigrationProcessResult(int ExitCode, string Output, bool HttpPortObserved);
}

public sealed class ControlledMigrationCommandLineTests
{
    [Fact]
    public async Task HealthCheckAndMigrationTarget_AreRejectedBeforeHttpOrDatabaseAccess()
    {
        var result = await ControlledMigrationTests.RunApiProcessAsync(
            connectionString: null,
            enabledValue: "true",
            target: "20260913020000_CashRegisterModuleV1",
            includeHealthCheck: true);

        Assert.Equal(ControlledMigrationRunner.RejectedExitCode, result.ExitCode);
        Assert.Contains("Los modos --health-check y --migrate-to no se pueden combinar.", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.DoesNotContain("applied successfully", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings__ControlPlusDb", result.Output, StringComparison.Ordinal);
    }
}
