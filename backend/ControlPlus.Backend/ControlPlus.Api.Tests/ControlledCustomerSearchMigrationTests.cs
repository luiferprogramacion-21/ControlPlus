using ControlPlus.Api.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

[Collection(ControlledMigrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ControlledCustomerSearchMigrationTests(ControlledMigrationPostgresFixture postgres)
{
    private const string InventoryMigration = InventoryMigrationManifest.TargetMigration;
    private const string TargetMigration = CustomerSearchMigrationManifest.TargetMigration;

    [Fact]
    public void Manifest_DefinesExactHistoryTableAndIndexContracts()
    {
        Assert.Equal(
            [
                "20260912010000_OfficialPhase4Baseline",
                "20260912011000_SeedApprovedSecurityCatalog",
                "20260912012000_HybridPermissionsV1",
                "20260913010000_SeedMeasurementUnitsV1",
                CashRegisterPreflightManifest.TargetMigration,
                InventoryMigration
            ],
            CustomerSearchMigrationManifest.RequiredPreviousMigrations);
        Assert.Equal(14, CustomerSearchMigrationManifest.RequiredColumns.Count);
        Assert.Equal(
            [
                "ix_cliente_busqueda_nombre_prefijo",
                "ix_cliente_busqueda_documento_prefijo",
                "ix_cliente_busqueda_telefono_prefijo",
                "ix_cliente_activo_nombre_id"
            ],
            CustomerSearchMigrationManifest.Indexes.Select(index => index.Name));
        Assert.All(
            CustomerSearchMigrationManifest.Indexes,
            index =>
            {
                Assert.StartsWith("CREATE INDEX ", index.Definition, StringComparison.Ordinal);
                Assert.Contains(" ON catalogo.cliente USING btree ", index.Definition, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Preflight_WithSixExactPredecessors_IsReadOnlyAndNeverStartsHttp()
    {
        var connectionString = await CreateReadyDatabaseAsync();
        var historyBefore = await HistorySnapshotAsync(connectionString);
        var indexesBefore = await IndexSnapshotAsync(connectionString);

        var result = await RunAsync(
            connectionString,
            ControlledExecutionCommand.PreflightTargetArgument);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("REPEATABLE READ, READ ONLY", result.Output, StringComparison.Ordinal);
        Assert.Contains("historial=6/6/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("indices_reservados=0/4", result.Output, StringComparison.Ordinal);
        Assert.Contains("rollback=confirmed", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(historyBefore, await HistorySnapshotAsync(connectionString));
        Assert.Equal(indexesBefore, await IndexSnapshotAsync(connectionString));
    }

    [Fact]
    public async Task Preflight_RejectsMissingAppliedExtraOrIncoherentHistory()
    {
        var missing = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(
            missing,
            CashRegisterPreflightManifest.TargetMigration);
        AssertRejected(
            await RunAsync(missing, ControlledExecutionCommand.PreflightTargetArgument),
            "six required predecessor migrations");

        var applied = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(applied, TargetMigration);
        AssertRejected(
            await RunAsync(applied, ControlledExecutionCommand.PreflightTargetArgument),
            "six required predecessor migrations");

        var extra = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ExecuteAsync(
            extra,
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20990101000000_UnexpectedMigration', '10.0.3')
            """);
        AssertRejected(
            await RunAsync(extra, ControlledExecutionCommand.PreflightTargetArgument),
            "six required predecessor migrations");

        var incoherent = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ExecuteAsync(
            incoherent,
            """
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId"='20260912012000_HybridPermissionsV1'
            """);
        AssertRejected(
            await RunAsync(incoherent, ControlledExecutionCommand.PreflightTargetArgument),
            "six required predecessor migrations");
    }

    [Fact]
    public async Task Preflight_RejectsWrongTableShapeAndEveryReservedIndex()
    {
        var wrongColumn = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ExecuteAsync(
            wrongColumn,
            "ALTER TABLE catalogo.cliente ALTER COLUMN nombre_completo DROP NOT NULL");
        AssertRejected(
            await RunAsync(wrongColumn, ControlledExecutionCommand.PreflightTargetArgument),
            "exact required shape");

        foreach (var index in CustomerSearchMigrationManifest.Indexes)
        {
            var connectionString = await CreateReadyDatabaseAsync();
            await ControlledMigrationTests.ExecuteAsync(
                connectionString,
                $"CREATE INDEX {index.Name} ON catalogo.cliente(id)");
            AssertRejected(
                await RunAsync(connectionString, ControlledExecutionCommand.PreflightTargetArgument),
                "reserved for the target already exists");
        }
    }

    [Fact]
    public async Task Preflight_RejectsReservedIndexOnAnotherCatalogoTableWithoutChangingDatabase()
    {
        foreach (var index in CustomerSearchMigrationManifest.Indexes)
        {
            var connectionString = await CreateReadyDatabaseAsync();
            await ControlledMigrationTests.ExecuteAsync(
                connectionString,
                $"CREATE INDEX {index.Name} ON catalogo.categoria(id)");
            var historyBefore = await HistorySnapshotAsync(connectionString);

            AssertRejected(
                await RunAsync(connectionString, ControlledExecutionCommand.PreflightTargetArgument),
                "reserved for the target already exists");
            Assert.Equal(historyBefore, await HistorySnapshotAsync(connectionString));
            Assert.Equal(0L, await TargetIndexCountAsync(connectionString));
        }
    }

    [Fact]
    public async Task Migration_RequiresInventoryAndAppliesOnlyTargetExactlyOnce()
    {
        var missing = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(
            missing,
            CashRegisterPreflightManifest.TargetMigration);
        var missingResult = await RunAsync(
            missing,
            ControlledExecutionCommand.MigrationTargetArgument);
        AssertRejected(missingResult, "require Inventory as the sixth and last applied migration");
        Assert.Equal(5L, await ControlledMigrationTests.HistoryCountAsync(missing));

        var ready = await CreateReadyDatabaseAsync();
        var result = await RunAsync(ready, ControlledExecutionCommand.MigrationTargetArgument);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("applied successfully", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(7L, await ControlledMigrationTests.HistoryCountAsync(ready));
        Assert.Equal(1L, await ControlledMigrationTests.MigrationCountAsync(ready, TargetMigration));
        Assert.Equal(4L, await TargetIndexCountAsync(ready));

        var repeated = await RunAsync(ready, ControlledExecutionCommand.MigrationTargetArgument);
        AssertRejected(repeated, "already applied");
        Assert.Equal(7L, await ControlledMigrationTests.HistoryCountAsync(ready));
        Assert.Equal(1L, await ControlledMigrationTests.MigrationCountAsync(ready, TargetMigration));
    }

    [Fact]
    public async Task Migration_RejectsIncoherentHistoryWithoutApplyingTarget()
    {
        var connectionString = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ExecuteAsync(
            connectionString,
            """
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId"='20260912012000_HybridPermissionsV1'
            """);

        var result = await RunAsync(
            connectionString,
            ControlledExecutionCommand.MigrationTargetArgument);

        AssertRejected(result, "not a coherent catalog prefix");
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(
            connectionString,
            TargetMigration));
        Assert.Equal(0L, await TargetIndexCountAsync(connectionString));
    }

    [Fact]
    public async Task Postflight_ValidatesExactHistoryAndIndexesReadOnlyWithoutHttp()
    {
        var connectionString = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, TargetMigration);
        var historyBefore = await HistorySnapshotAsync(connectionString);
        var indexesBefore = await IndexSnapshotAsync(connectionString);

        var result = await RunAsync(
            connectionString,
            ControlledExecutionCommand.PostflightTargetArgument);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("REPEATABLE READ, READ ONLY", result.Output, StringComparison.Ordinal);
        Assert.Contains("historial=7/7/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("indices=4/4/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("rollback=confirmed", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(historyBefore, await HistorySnapshotAsync(connectionString));
        Assert.Equal(indexesBefore, await IndexSnapshotAsync(connectionString));
    }

    [Fact]
    public async Task Postflight_RejectsMissingTargetAndPartialOrIncorrectIndexStructure()
    {
        var missingTarget = await CreateReadyDatabaseAsync();
        AssertRejected(
            await RunAsync(missingTarget, ControlledExecutionCommand.PostflightTargetArgument),
            "seven required migrations");

        var missingIndex = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(missingIndex, TargetMigration);
        await ControlledMigrationTests.ExecuteAsync(
            missingIndex,
            "DROP INDEX catalogo.ix_cliente_busqueda_telefono_prefijo");
        AssertRejected(
            await RunAsync(missingIndex, ControlledExecutionCommand.PostflightTargetArgument),
            "do not match the exact manifest");

        var incorrectIndex = await CreateReadyDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(incorrectIndex, TargetMigration);
        await ControlledMigrationTests.ExecuteAsync(
            incorrectIndex,
            """
            DROP INDEX catalogo.ix_cliente_busqueda_documento_prefijo;
            CREATE INDEX ix_cliente_busqueda_documento_prefijo
                ON catalogo.cliente(numero_documento)
            """);
        AssertRejected(
            await RunAsync(incorrectIndex, ControlledExecutionCommand.PostflightTargetArgument),
            "do not match the exact manifest");
    }

    [Theory]
    [InlineData("--preflight-to")]
    [InlineData("--migrate-to")]
    [InlineData("--postflight-to")]
    public async Task ControlledModes_ReturnSanitizedFailureWithoutHttp(string mode)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        const string sentinel = "DO-NOT-LEAK-CUSTOMER-INDEX-PASSWORD";
        var invalidConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = sentinel
        }.ConnectionString;

        var result = await RunAsync(invalidConnection, mode);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("failed", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidConnection, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(0L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
    }

    private async Task<string> CreateReadyDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, InventoryMigration);
        return connectionString;
    }

    private static Task<ControlledMigrationTests.MigrationProcessResult> RunAsync(
        string? connectionString,
        string mode) =>
        ControlledMigrationTests.RunApiProcessAsync(
            connectionString,
            "true",
            mode,
            TargetMigration);

    private static async Task<long> TargetIndexCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM pg_catalog.pg_indexes
            WHERE schemaname='catalogo'
              AND tablename='cliente'
              AND indexname = ANY(@names)
            """,
            connection);
        command.Parameters.AddWithValue(
            "names",
            CustomerSearchMigrationManifest.Indexes.Select(index => index.Name).ToArray());
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> HistorySnapshotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(string_agg("MigrationId" || '|' || "ProductVersion", E'\n' ORDER BY "MigrationId"), '')
            FROM "__EFMigrationsHistory"
            """,
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> IndexSnapshotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(string_agg(indexname || '|' || indexdef, E'\n' ORDER BY indexname), '')
            FROM pg_catalog.pg_indexes
            WHERE schemaname='catalogo' AND tablename='cliente'
            """,
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void AssertRejected(
        ControlledMigrationTests.MigrationProcessResult result,
        string message)
    {
        Assert.Equal(ControlledMigrationRunner.RejectedExitCode, result.ExitCode);
        Assert.Contains(message, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.HttpPortObserved);
    }
}
