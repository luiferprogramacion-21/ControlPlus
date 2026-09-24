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

        var result = await RunApiProcessAsync(
            connectionString,
            null,
            ControlledExecutionCommand.MigrationTargetArgument,
            CashMigration);

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

        var result = await RunApiProcessAsync(
            connectionString,
            value,
            ControlledExecutionCommand.MigrationTargetArgument,
            CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task MissingTargetArgument_IsRejectedWithoutDatabaseChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Command line rejected", result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task UnknownTarget_IsRejectedWithoutDatabaseChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument,
            "20990101000000_DoesNotExist");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("does not exist", result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task AlreadyAppliedTarget_IsRejectedWithoutAdditionalHistoryEntry()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ApplyAsync(connectionString, CashMigration);

        var result = await RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument,
            CashMigration);

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

        var result = await RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument,
            PreviousMigration);

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

        var result = await RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument,
            CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not a coherent catalog prefix", result.Output, StringComparison.Ordinal);
        Assert.Equal(5L, await HistoryCountAsync(connectionString));
        Assert.Equal(0L, await MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task ExactPendingCashTarget_IsAppliedOnce_AndMigrationModeNeverListensForHttp()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var result = await RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument,
            CashMigration);

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

        var result = await RunApiProcessAsync(
            invalidConnection,
            "true",
            ControlledExecutionCommand.MigrationTargetArgument,
            CashMigration);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"Migration target '{CashMigration}' failed", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidConnection, result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await HistoryCountAsync(connectionString));
    }

    internal static async Task ApplyAsync(string connectionString, string target)
    {
        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var context = new ControlPlusDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    internal static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<long> HistoryCountAsync(string connectionString)
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

    internal static async Task<long> MigrationCountAsync(string connectionString, string migration)
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
        params string[] arguments)
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
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
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

[Collection(ControlledMigrationCollection.Name)]
public sealed class ControlledPreflightTests(ControlledMigrationPostgresFixture postgres)
{
    private const string PreviousMigration = "20260913010000_SeedMeasurementUnitsV1";
    private const string CashMigration = ControlledPreflightRunner.CashMigration;

    [Fact]
    public void Manifest_MatchesCashRegisterModuleV1Contract()
    {
        Assert.Equal(
            [
                "caja.autorizacion_cierre_turno",
                "caja.detalle_arqueo_medio_pago"
            ],
            CashRegisterPreflightManifest.CreatedTables.Select(item => $"{item.Schema}.{item.Name}"));
        Assert.Equal(
            "caja.turno_caja.diferencia_total",
            $"{CashRegisterPreflightManifest.CreatedDifferenceTotalColumn.Schema}." +
            $"{CashRegisterPreflightManifest.CreatedDifferenceTotalColumn.Table}." +
            CashRegisterPreflightManifest.CreatedDifferenceTotalColumn.Column);
        Assert.Equal(
            [
                "caja.ix_detalle_arqueo_metodo_pago:detalle_arqueo_medio_pago:metodo_pago_id",
                "ventas.ix_pago_venta_movimiento_caja:pago_venta:movimiento_caja_id",
                "ventas.ix_pago_credito_movimiento_caja:pago_credito:movimiento_caja_id",
                "ventas.ix_pago_apartado_movimiento_caja:pago_apartado:movimiento_caja_id",
                "ventas.ix_pago_cambio_movimiento_caja:pago_cambio_venta:movimiento_caja_id",
                "ventas.ix_reembolso_apartado_movimiento:reembolso_apartado:movimiento_caja_id"
            ],
            CashRegisterPreflightManifest.CreatedIndexes.Select(
                item => $"{item.Schema}.{item.Name}:{item.Table}:{string.Join(',', item.Columns)}"));
        Assert.Equal(
            [
                "caja.proteger_detalle_arqueo():trigger",
                "caja.proteger_turno_cerrado():trigger",
                "caja.proteger_metodo_arqueado():trigger",
                "caja.vincular_autorizacion_cierre():trigger",
                "caja.proteger_autorizacion_consumida():trigger",
                "caja.proteger_contexto_cierre():trigger",
                "caja.validar_integridad_arqueo(uuid):void",
                "caja.validar_arqueo_desde_turno():trigger",
                "caja.validar_arqueo_desde_detalle():trigger"
            ],
            CashRegisterPreflightManifest.CreatedFunctions.Select(
                item => $"{item.Schema}.{item.Name}({string.Join(',', item.ArgumentTypes)}):{item.ReturnType}"));
        Assert.Equal(
            [
                "caja.detalle_arqueo_medio_pago.tg_detalle_proteger_arqueo",
                "caja.turno_caja.tg_turno_proteger_cierre",
                "catalogo.metodo_pago.tg_metodo_proteger_arqueo",
                "caja.autorizacion_cierre_turno.tg_autorizacion_vincular_cierre",
                "seguridad.autorizacion_operacion.tg_autorizacion_proteger_consumida",
                "configuracion.instalacion.tg_instalacion_proteger_contexto_cierre",
                "caja.caja.tg_caja_proteger_contexto_cierre",
                "configuracion.terminal.tg_terminal_proteger_contexto_cierre",
                "caja.turno_caja.tg_turno_validar_integridad_arqueo",
                "caja.detalle_arqueo_medio_pago.tg_detalle_validar_integridad_arqueo",
                "caja.autorizacion_cierre_turno.tg_autorizacion_validar_integridad_cierre"
            ],
            CashRegisterPreflightManifest.CreatedTriggers.Select(
                item => $"{item.Schema}.{item.Table}.{item.Name}"));
        Assert.Equal(
            [
                "caja.turno_caja.ck_turno_caja_diferencia:Check:true:diferencia,efectivo_contado,efectivo_esperado",
                "caja.turno_caja.ck_turno_caja_motivo_diferencia:Check:true:diferencia,motivo_diferencia_id",
                "ventas.pago_venta.uq_pago_venta_movimiento_caja:Unique:true:movimiento_caja_id",
                "ventas.pago_credito.uq_pago_credito_movimiento_caja:Unique:true:movimiento_caja_id",
                "ventas.pago_apartado.uq_pago_apartado_movimiento_caja:Unique:true:movimiento_caja_id",
                "ventas.pago_cambio_venta.uq_pago_cambio_movimiento_caja:Unique:true:movimiento_caja_id",
                "ventas.reembolso_apartado.uq_reembolso_apartado_movimiento:Unique:true:movimiento_caja_id"
            ],
            CashRegisterPreflightManifest.RequiredBaseConstraints.Select(
                item => $"{item.Schema}.{item.Table}.{item.Name}:{item.Kind}:" +
                        $"{item.MustBeValidated.ToString().ToLowerInvariant()}:{string.Join(',', item.Columns)}"));
    }

    [Fact]
    public async Task ExactCashPreflight_CertifiesWithoutChanges_AndNeverListensForHttp()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);

        var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("certified in a read-only transaction", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(
            0L,
            await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("false")]
    public async Task InvalidGate_IsRejectedWithoutDatabaseChanges(string? enabledValue)
    {
        var result = await RunPreflightProcessAsync(null, enabledValue, CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("must be exactly true", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.DoesNotContain("database configuration", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("20990101000000_DoesNotExist")]
    [InlineData("20260913010000_SeedMeasurementUnitsV1")]
    [InlineData("invalid-target;SELECT")]
    public async Task InvalidTarget_IsRejectedWithoutDatabaseChanges(string target)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);

        var result = await RunPreflightProcessAsync(connectionString, "true", target);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("rejected", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(
            0L,
            await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));
        Assert.DoesNotContain("SELECT", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppliedTarget_IsRejectedWithoutAdditionalChanges()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, CashMigration);

        var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("already applied", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(5L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(
            1L,
            await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task ExistingShift_IsRejectedWithoutChangingIt()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);
        await SeedShiftAsync(connectionString);

        var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("caja.turno_caja is not empty", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM caja.turno_caja"));
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task EveryReservedCashStructure_IsRejectedWithoutApplyingTarget()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);
        await ControlledMigrationTests.ExecuteAsync(
            connectionString,
            """
            CREATE TABLE caja.preflight_index_host(
                metodo_pago_id uuid,
                movimiento_caja_id uuid);
            CREATE TABLE ventas.preflight_index_host(movimiento_caja_id uuid);
            CREATE FUNCTION caja.preflight_test_trigger()
            RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NEW; END'
            """);

        foreach (var table in CashRegisterPreflightManifest.CreatedTables)
        {
            var qualifiedName = $"{table.Schema}.{table.Name}";
            await AssertReservedStructureRejectedAsync(
                connectionString,
                $"table {qualifiedName}",
                $"CREATE TABLE {qualifiedName} (id integer)",
                $"DROP TABLE {qualifiedName}",
                "table");
        }

        var column = CashRegisterPreflightManifest.CreatedDifferenceTotalColumn;
        await AssertReservedStructureRejectedAsync(
            connectionString,
            $"column {column.Schema}.{column.Table}.{column.Column}",
            $"ALTER TABLE {column.Schema}.{column.Table} ADD COLUMN {column.Column} numeric(18,0)",
            $"ALTER TABLE {column.Schema}.{column.Table} DROP COLUMN {column.Column}",
            "column");

        foreach (var index in CashRegisterPreflightManifest.CreatedIndexes)
        {
            var hostTable = index.Schema == "caja"
                ? "caja.preflight_index_host"
                : "ventas.preflight_index_host";
            await AssertReservedStructureRejectedAsync(
                connectionString,
                $"index {index.Schema}.{index.Name}",
                $"CREATE INDEX {index.Name} ON {hostTable}({index.Columns[0]})",
                $"DROP INDEX {index.Schema}.{index.Name}",
                "index");
        }

        foreach (var function in CashRegisterPreflightManifest.CreatedFunctions)
        {
            var arguments = string.Join(',', function.ArgumentTypes);
            var body = function.ReturnType == "trigger"
                ? "BEGIN RETURN NEW; END"
                : "BEGIN RETURN; END";
            await AssertReservedStructureRejectedAsync(
                connectionString,
                $"function {function.Schema}.{function.Name}({arguments})",
                $"CREATE FUNCTION {function.Schema}.{function.Name}({arguments}) " +
                $"RETURNS {function.ReturnType} LANGUAGE plpgsql AS '{body}'",
                $"DROP FUNCTION {function.Schema}.{function.Name}({arguments})",
                "function");
        }

        foreach (var trigger in CashRegisterPreflightManifest.CreatedTriggers)
        {
            var tableIsCreatedByTarget = CashRegisterPreflightManifest.CreatedTables.Any(
                table => table.Schema == trigger.Schema && table.Name == trigger.Table);
            var qualifiedTable = $"{trigger.Schema}.{trigger.Table}";
            if (tableIsCreatedByTarget)
            {
                await ControlledMigrationTests.ExecuteAsync(
                    connectionString,
                    $"CREATE TABLE {qualifiedTable} (id integer)");
            }

            await AssertReservedStructureRejectedAsync(
                connectionString,
                $"trigger {qualifiedTable}.{trigger.Name}",
                $"CREATE TRIGGER {trigger.Name} BEFORE UPDATE ON {qualifiedTable} " +
                "FOR EACH ROW EXECUTE FUNCTION caja.preflight_test_trigger()",
                $"DROP TRIGGER {trigger.Name} ON {qualifiedTable}",
                "trigger");

            if (tableIsCreatedByTarget)
            {
                await ControlledMigrationTests.ExecuteAsync(
                    connectionString,
                    $"DROP TABLE {qualifiedTable}");
            }
        }

        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(
            0L,
            await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task EveryReservedFunctionIdentity_WithIncompatibleReturn_IsRejected()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);

        foreach (var function in CashRegisterPreflightManifest.CreatedFunctions)
        {
            var arguments = string.Join(',', function.ArgumentTypes);
            await ControlledMigrationTests.ExecuteAsync(
                connectionString,
                $"CREATE FUNCTION {function.Schema}.{function.Name}({arguments}) " +
                "RETURNS integer LANGUAGE sql AS 'SELECT 1'");

            var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

            Assert.True(
                result.ExitCode == 2,
                $"Function {function.Schema}.{function.Name}({arguments}): {result.Output}");
            Assert.Contains("function", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.HttpPortObserved);
            Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
            Assert.Equal(
                0L,
                await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));

            await ControlledMigrationTests.ExecuteAsync(
                connectionString,
                $"DROP FUNCTION {function.Schema}.{function.Name}({arguments})");
        }
    }

    [Fact]
    public async Task EveryMissingRequiredBaseConstraint_IsRejectedWithoutApplyingTarget()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);

        foreach (var constraint in CashRegisterPreflightManifest.RequiredBaseConstraints)
        {
            await ControlledMigrationTests.ExecuteAsync(
                connectionString,
                $"ALTER TABLE {constraint.Schema}.{constraint.Table} DROP CONSTRAINT {constraint.Name}");

            var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

            Assert.True(
                result.ExitCode == 2,
                $"Constraint {constraint.Schema}.{constraint.Table}.{constraint.Name}: {result.Output}");
            Assert.Contains("seven required base constraints", result.Output, StringComparison.Ordinal);
            Assert.False(result.HttpPortObserved);

            await ControlledMigrationTests.ExecuteAsync(
                connectionString,
                CreateRequiredConstraintSql(constraint));
        }

        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(
            0L,
            await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));
    }

    [Fact]
    public async Task RequiredConstraint_TypeColumnsAndValidationMustMatchManifest()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);
        var uniqueConstraint = CashRegisterPreflightManifest.RequiredBaseConstraints.Single(
            item => item.Name == "uq_pago_venta_movimiento_caja");
        var checkConstraint = CashRegisterPreflightManifest.RequiredBaseConstraints.Single(
            item => item.Name == "ck_turno_caja_diferencia");

        await AssertMalformedConstraintRejectedAsync(
            connectionString,
            uniqueConstraint,
            """
            ALTER TABLE ventas.pago_venta
            ADD CONSTRAINT uq_pago_venta_movimiento_caja
            CHECK (movimiento_caja_id IS NULL OR movimiento_caja_id IS NOT NULL)
            """);
        await AssertMalformedConstraintRejectedAsync(
            connectionString,
            uniqueConstraint,
            """
            ALTER TABLE ventas.pago_venta
            ADD CONSTRAINT uq_pago_venta_movimiento_caja UNIQUE (id)
            """);
        await AssertMalformedConstraintRejectedAsync(
            connectionString,
            checkConstraint,
            """
            ALTER TABLE caja.turno_caja
            ADD CONSTRAINT ck_turno_caja_diferencia CHECK (
                diferencia IS NULL
                OR diferencia = efectivo_contado - efectivo_esperado) NOT VALID
            """);
    }

    [Fact]
    public async Task ProviderFailure_IsSanitizedAndReturnsTechnicalFailure()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, PreviousMigration);
        const string sentinel = "DO-NOT-LEAK-PREFLIGHT-PASSWORD";
        var invalidConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = sentinel
        }.ConnectionString;

        var result = await RunPreflightProcessAsync(invalidConnection, "true", CashMigration);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("failed without changing the database", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidConnection, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", result.Output, StringComparison.Ordinal);
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
    }

    private static async Task AssertReservedStructureRejectedAsync(
        string connectionString,
        string label,
        string createSql,
        string dropSql,
        string expectedKind)
    {
        await ControlledMigrationTests.ExecuteAsync(connectionString, createSql);

        var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

        Assert.True(result.ExitCode == 2, $"{label}: {result.Output}");
        Assert.Contains(expectedKind, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(
            0L,
            await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));

        await ControlledMigrationTests.ExecuteAsync(connectionString, dropSql);
    }

    private static async Task AssertMalformedConstraintRejectedAsync(
        string connectionString,
        PreflightRequiredConstraint constraint,
        string malformedConstraintSql)
    {
        await ControlledMigrationTests.ExecuteAsync(
            connectionString,
            $"ALTER TABLE {constraint.Schema}.{constraint.Table} DROP CONSTRAINT {constraint.Name}");
        await ControlledMigrationTests.ExecuteAsync(connectionString, malformedConstraintSql);

        var result = await RunPreflightProcessAsync(connectionString, "true", CashMigration);

        Assert.True(
            result.ExitCode == 2,
            $"Malformed constraint {constraint.Schema}.{constraint.Table}.{constraint.Name}: {result.Output}");
        Assert.Contains("seven required base constraints", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);

        await ControlledMigrationTests.ExecuteAsync(
            connectionString,
            $"ALTER TABLE {constraint.Schema}.{constraint.Table} DROP CONSTRAINT {constraint.Name}");
        await ControlledMigrationTests.ExecuteAsync(
            connectionString,
            CreateRequiredConstraintSql(constraint));
    }

    private static string CreateRequiredConstraintSql(PreflightRequiredConstraint constraint) =>
        constraint.Name switch
        {
            "ck_turno_caja_diferencia" =>
                """
                ALTER TABLE caja.turno_caja
                ADD CONSTRAINT ck_turno_caja_diferencia CHECK (
                    diferencia IS NULL
                    OR diferencia = efectivo_contado - efectivo_esperado)
                """,
            "ck_turno_caja_motivo_diferencia" =>
                """
                ALTER TABLE caja.turno_caja
                ADD CONSTRAINT ck_turno_caja_motivo_diferencia CHECK (
                    diferencia IS NULL
                    OR (diferencia = 0 AND motivo_diferencia_id IS NULL)
                    OR (diferencia <> 0 AND motivo_diferencia_id IS NOT NULL))
                """,
            _ when constraint.Kind == PreflightConstraintKind.Unique =>
                $"ALTER TABLE {constraint.Schema}.{constraint.Table} " +
                $"ADD CONSTRAINT {constraint.Name} UNIQUE ({string.Join(',', constraint.Columns)})",
            _ => throw new InvalidOperationException($"Unsupported test constraint {constraint.Name}.")
        };

    private static Task<ControlledMigrationTests.MigrationProcessResult> RunPreflightProcessAsync(
        string? connectionString,
        string? enabledValue,
        string target) =>
        ControlledMigrationTests.RunApiProcessAsync(
            connectionString,
            enabledValue,
            ControlledExecutionCommand.PreflightTargetArgument,
            target);

    private static Task SeedShiftAsync(string connectionString) =>
        ControlledMigrationTests.ExecuteAsync(
            connectionString,
            """
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES ('01950000-0000-7000-8000-000000000001','Preflight test','PREFLIGHT');
            INSERT INTO configuracion.instalacion(
                id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
            VALUES (
                '01950000-0000-7000-8000-000000000002',
                '01950000-0000-7000-8000-000000000001',
                'PREFLIGHT-INSTALL','TEST','test','test');
            INSERT INTO configuracion.terminal(id,instalacion_id,codigo,nombre)
            VALUES (
                '01950000-0000-7000-8000-000000000003',
                '01950000-0000-7000-8000-000000000002',
                'PREFLIGHT-TERMINAL','Terminal preflight');
            INSERT INTO caja.caja(id,instalacion_id,codigo,nombre)
            VALUES (
                '01950000-0000-7000-8000-000000000004',
                '01950000-0000-7000-8000-000000000002',
                'PREFLIGHT-CASH','Caja preflight');
            INSERT INTO seguridad.usuario(
                id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,
                password_hash,sello_seguridad,sello_concurrencia)
            VALUES (
                '01950000-0000-7000-8000-000000000005',
                'preflight','PREFLIGHT','Usuario preflight','unused-test-fixture','test','test');
            INSERT INTO caja.turno_caja(
                id,caja_id,terminal_id,modo_operacion,usuario_apertura_id,
                usuario_responsable_id,fecha_operativa,monto_inicial)
            VALUES (
                '01950000-0000-7000-8000-000000000006',
                '01950000-0000-7000-8000-000000000004',
                '01950000-0000-7000-8000-000000000003',
                'INDIVIDUAL','01950000-0000-7000-8000-000000000005',
                '01950000-0000-7000-8000-000000000005',CURRENT_DATE,0)
            """);

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}

public sealed class ControlledMigrationCommandLineTests
{
    private const string CashMigration = CashRegisterPreflightManifest.TargetMigration;
    private const string OtherMigration = "20990101000000_OtherMigration";

    public static IEnumerable<object[]> RejectedArgumentMatrix()
    {
        yield return ["unknown option", new[] { "--unknown" }];
        yield return ["unexpected positional argument", new[] { "unexpected" }];
        yield return ["health-check extra argument", new[] { "--health-check", "extra" }];
        yield return ["health-check equals syntax", new[] { "--health-check=true" }];
        yield return ["migration target missing", new[] { "--migrate-to" }];
        yield return ["preflight target missing", new[] { "--preflight-to" }];
        yield return ["migration target starts with option", new[] { "--migrate-to", "--unknown" }];
        yield return ["preflight target starts with option", new[] { "--preflight-to", "--unknown" }];
        yield return ["migration equals syntax", new[] { $"--migrate-to={CashMigration}" }];
        yield return ["preflight equals syntax", new[] { $"--preflight-to={CashMigration}" }];
        yield return
        [
            "health-check with migration",
            new[] { "--health-check", "--migrate-to", CashMigration }
        ];
        yield return
        [
            "health-check with preflight",
            new[] { "--health-check", "--preflight-to", CashMigration }
        ];
        yield return
        [
            "migration with preflight",
            new[] { "--migrate-to", CashMigration, "--preflight-to", CashMigration }
        ];
        yield return
        [
            "duplicate migration option",
            new[] { "--migrate-to", CashMigration, "--migrate-to", CashMigration }
        ];
        yield return
        [
            "duplicate preflight option",
            new[] { "--preflight-to", CashMigration, "--preflight-to", CashMigration }
        ];
        yield return
        [
            "migration duplicate targets",
            new[] { "--migrate-to", CashMigration, OtherMigration }
        ];
        yield return
        [
            "preflight duplicate targets",
            new[] { "--preflight-to", CashMigration, OtherMigration }
        ];
        yield return
        [
            "migration unknown trailing option",
            new[] { "--migrate-to", CashMigration, "--unknown" }
        ];
        yield return
        [
            "preflight unknown trailing option",
            new[] { "--preflight-to", CashMigration, "--unknown" }
        ];
        yield return
        [
            "preflight with migration equals syntax",
            new[] { "--preflight-to", CashMigration, $"--migrate-to={OtherMigration}" }
        ];
        yield return
        [
            "preflight with health-check equals syntax",
            new[] { "--preflight-to", CashMigration, "--health-check=true" }
        ];
    }

    public static IEnumerable<object[]> InvalidMigrationIdMatrix()
    {
        var invalidIds = new (string Name, string Value)[]
        {
            ("equals", "="),
            ("semicolon", ";"),
            ("spaces", "migration id"),
            ("non-ASCII", "Migración"),
            ("over 200 characters", new string('A', 201)),
            ("empty", string.Empty),
            ("option prefix", "--invalid")
        };
        var modes = new[]
        {
            ControlledExecutionCommand.MigrationTargetArgument,
            ControlledExecutionCommand.PreflightTargetArgument
        };

        foreach (var mode in modes)
        {
            foreach (var invalidId in invalidIds)
            {
                yield return [mode, invalidId.Name, invalidId.Value];
            }
        }
    }

    public static IEnumerable<object[]> MigrationIdLexicalMatrix()
    {
        yield return [string.Empty, false];
        yield return [" ", false];
        yield return ["--invalid", false];
        yield return ["migration id", false];
        yield return ["Migration-Id", false];
        yield return ["Migración", false];
        yield return [new string('A', 201), false];
        yield return ["A", true];
        yield return ["_", true];
        yield return ["20260913020000_CashRegisterModuleV1", true];
        yield return [new string('A', 200), true];
    }

    [Theory]
    [MemberData(nameof(MigrationIdLexicalMatrix))]
    public void IsValidMigrationId_IsTheLexicalAuthority(string migrationId, bool expected)
    {
        Assert.Equal(expected, ControlledExecutionCommand.IsValidMigrationId(migrationId));
    }

    [Theory]
    [MemberData(nameof(RejectedArgumentMatrix))]
    public async Task RejectedCliGrammar_StopsBeforeHttpOrDatabaseAccess(
        string caseName,
        string[] arguments)
    {
        var result = await ControlledMigrationTests.RunApiProcessAsync(
            connectionString: null,
            enabledValue: "true",
            arguments: arguments);

        Assert.True(
            result.ExitCode == ControlledMigrationRunner.RejectedExitCode,
            $"{caseName}: {result.Output}");
        Assert.Contains("Command line rejected", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.DoesNotContain("applied successfully", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database configuration", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings__ControlPlusDb", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidMigrationIdMatrix))]
    public async Task InvalidMigrationId_IsRejectedBeforeConfigurationDatabaseOrHttp(
        string mode,
        string caseName,
        string invalidMigrationId)
    {
        var result = await ControlledMigrationTests.RunApiProcessAsync(
            connectionString: null,
            enabledValue: "true",
            arguments: [mode, invalidMigrationId]);

        Assert.True(
            result.ExitCode == ControlledMigrationRunner.RejectedExitCode,
            $"{mode} / {caseName}: {result.Output}");
        Assert.Contains("Command line rejected", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.DoesNotContain("database configuration", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings__ControlPlusDb", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", result.Output, StringComparison.Ordinal);
    }
}
