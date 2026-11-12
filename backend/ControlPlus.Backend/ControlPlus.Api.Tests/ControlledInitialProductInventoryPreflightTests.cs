using ControlPlus.Api.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

[Collection(ControlledMigrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ControlledInitialProductInventoryPreflightTests(ControlledMigrationPostgresFixture postgres)
{
    private const string Previous = "20261021010000_LayawayCancellationOptionalReasonV1";
    private const string Target = ControlledMigrationRunner.InitialProductInventoryMigration;

    [Fact, Trait("Category", "Integration")]
    public async Task Preflight_ExactHistoryAndOfficialConstraint_IsReadOnlyAndNeverStartsHttp()
    {
        var connection = await ReadyAsync();
        var result = await RunAsync(connection);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("REPEATABLE READ, READ ONLY", result.Output, StringComparison.Ordinal);
        Assert.Contains("historial=10/10/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("objetos_reservados=0/2", result.Output, StringComparison.Ordinal);
        Assert.Contains("rollback=confirmed", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(10L, await ControlledMigrationTests.HistoryCountAsync(connection));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(connection, Target));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Preflight_RejectsMissingOrAlreadyAppliedHistory()
    {
        var missing = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(missing, "20261014010000_InventoryMovementRegistrationOrderV1");
        AssertRejected(await RunAsync(missing), "ten required predecessor migrations");
        Assert.Equal(8L, await ControlledMigrationTests.HistoryCountAsync(missing));

        var applied = await ReadyAsync();
        await ControlledMigrationTests.ApplyAsync(applied, Target);
        AssertRejected(await RunAsync(applied), "ten required predecessor migrations");
        Assert.Equal(11L, await ControlledMigrationTests.HistoryCountAsync(applied));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("ALTER TABLE compras.movimiento_inventario DROP CONSTRAINT ck_movimiento_inventario_tipo")]
    [InlineData("ALTER TABLE compras.movimiento_inventario DROP CONSTRAINT ck_movimiento_inventario_tipo; ALTER TABLE compras.movimiento_inventario ADD CONSTRAINT ck_movimiento_inventario_tipo CHECK (tipo_movimiento <> '')")]
    public async Task Preflight_RejectsAbsentOrIncompatibleOfficialConstraint(string sql)
    {
        var connection = await ReadyAsync();
        await ControlledMigrationTests.ExecuteAsync(connection, sql);
        AssertRejected(await RunAsync(connection), "type constraint is absent or incompatible");
        Assert.Equal(10L, await ControlledMigrationTests.HistoryCountAsync(connection));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("COM PRA")]
    [InlineData("COM(PRA")]
    [InlineData("COMPRA::text")]
    public async Task Preflight_RejectsChangesInsideOfficialLiteral(string literal)
    {
        var connection = await ReadyAsync();
        // Values are fixed test data, never application input.
        await ControlledMigrationTests.ExecuteAsync(connection, $"""
            ALTER TABLE compras.movimiento_inventario DROP CONSTRAINT ck_movimiento_inventario_tipo;
            ALTER TABLE compras.movimiento_inventario ADD CONSTRAINT ck_movimiento_inventario_tipo CHECK (
                tipo_movimiento IN ('{literal}', 'ANULACION_COMPRA', 'VENTA', 'ANULACION_VENTA',
                    'RESERVA_APARTADO', 'LIBERACION_APARTADO', 'ENTREGA_APARTADO',
                    'CAMBIO_DEVOLUCION', 'CAMBIO_ENTREGA', 'AJUSTE_ENTRADA', 'AJUSTE_SALIDA', 'REVERSO'));
            """);
        AssertRejected(await RunAsync(connection), "type constraint is absent or incompatible");
        Assert.Equal(10L, await ControlledMigrationTests.HistoryCountAsync(connection));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(connection, Target));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_LateIndexConflict_RollsBackConstraintChangesAndHistory()
    {
        var connectionString = await ReadyAsync();
        await ControlledMigrationTests.ExecuteAsync(connectionString,
            "CREATE INDEX uq_movimiento_inventario_inicial_producto ON compras.movimiento_inventario(id)");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string stateSql = """
            SELECT pg_get_constraintdef(c.oid),
                   to_regclass('compras.uq_movimiento_inventario_inicial_producto')::oid::bigint,
                   EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = c.conrelid
                           AND conname = 'ck_movimiento_inventario_inicial')
            FROM pg_constraint c WHERE c.conrelid = 'compras.movimiento_inventario'::regclass
              AND c.conname = 'ck_movimiento_inventario_tipo'
            """;
        static async Task<(string Definition, long IndexOid, bool NewConstraint)> StateAsync(
            NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetInt64(1), reader.GetBoolean(2));
        }
        var before = await StateAsync(connection, stateSql);
        Assert.False(before.NewConstraint);

        var result = await ControlledMigrationTests.RunApiProcessAsync(connectionString, "true",
            ControlledExecutionCommand.MigrationTargetArgument, Target);
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(before, await StateAsync(connection, stateSql));
        Assert.Equal(10L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(connectionString, Target));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("CREATE INDEX uq_movimiento_inventario_inicial_producto ON compras.movimiento_inventario(id)")]
    [InlineData("ALTER TABLE compras.movimiento_inventario ADD CONSTRAINT ck_movimiento_inventario_inicial CHECK (true)")]
    public async Task Preflight_RejectsReservedObjectsWithoutChanges(string sql)
    {
        var connection = await ReadyAsync();
        await ControlledMigrationTests.ExecuteAsync(connection, sql);
        AssertRejected(await RunAsync(connection), "object reserved for the target already exists");
        Assert.Equal(10L, await ControlledMigrationTests.HistoryCountAsync(connection));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(connection, Target));
    }

    private async Task<string> ReadyAsync()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connection, Previous);
        return connection;
    }

    private static Task<ControlledMigrationTests.MigrationProcessResult> RunAsync(string connection) =>
        ControlledMigrationTests.RunApiProcessAsync(connection, "true", ControlledExecutionCommand.PreflightTargetArgument, Target);

    private static void AssertRejected(ControlledMigrationTests.MigrationProcessResult result, string reason)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(reason, result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
    }
}
