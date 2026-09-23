using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class CashMigrationConcurrencyTests
{
    private const long MigrationGate = 720260913020000;
    private const string MigrationApplication = "cash-migration-race";
    private const string InsertApplication = "cash-concurrent-insert";

    [Fact]
    public async Task Up_HoldsAccessExclusiveFromPreconditionThroughDdl_AndBlocksConcurrentShiftInsert()
    {
        await using var database = await CashMigrationDatabase.StartAsync(applyCash: false);
        await database.SeedContextAsync();
        await using var coordinator = await database.OpenConnectionAsync();
        await using var observer = await database.OpenConnectionAsync();
        await using var inserter = await OpenNamedConnectionAsync(database, InsertApplication);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task? migrationTask = null;
        Task<int>? insertTask = null;
        var advisoryLockHeld = false;

        try
        {
            await CashMigrationDatabase.ExecuteAsync(coordinator, $"""
                CREATE FUNCTION public.test_pause_cash_migration_ddl()
                RETURNS event_trigger LANGUAGE plpgsql AS $function$
                BEGIN
                  IF current_setting('application_name') = '{MigrationApplication}'
                     AND TG_TAG = 'ALTER TABLE' THEN
                    PERFORM pg_advisory_xact_lock({MigrationGate});
                  END IF;
                END;
                $function$;
                CREATE EVENT TRIGGER test_pause_cash_migration_ddl
                  ON ddl_command_start WHEN TAG IN ('ALTER TABLE')
                  EXECUTE FUNCTION public.test_pause_cash_migration_ddl();
                SELECT pg_advisory_lock({MigrationGate});
                """);
            advisoryLockHeld = true;

            var migrationConnectionString = NamedConnectionString(database, MigrationApplication);
            migrationTask = MigrateAsync(migrationConnectionString, timeout.Token);
            await WaitForRelationLockAsync(observer, MigrationApplication,
                "AccessExclusiveLock", granted: true, timeout.Token);
            Assert.False(migrationTask.IsCompleted);

            await using var insert = new NpgsqlCommand($"""
                INSERT INTO caja.turno_caja
                  (id,caja_id,terminal_id,modo_operacion,usuario_apertura_id,
                   usuario_responsable_id,fecha_operativa,monto_inicial,observaciones)
                VALUES (gen_random_uuid(),'{CashMigrationDatabase.Register}',
                  '{CashMigrationDatabase.Terminal}','INDIVIDUAL','{CashMigrationDatabase.Executor}',
                  '{CashMigrationDatabase.Executor}',CURRENT_DATE,25,'Concurrente tras precondicion');
                """, inserter);
            insertTask = insert.ExecuteNonQueryAsync(timeout.Token);
            await WaitForRelationLockAsync(observer, InsertApplication,
                "RowExclusiveLock", granted: false, timeout.Token);
            Assert.False(insertTask.IsCompleted);

            Assert.True(await ReleaseAdvisoryLockAsync(coordinator));
            advisoryLockHeld = false;
            await migrationTask;
            Assert.Equal(1, await insertTask);
            await DropTestEventTriggerAsync(database.Connection);

            Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.turno_caja"));
            Assert.Equal("Concurrente tras precondicion", await database.ScalarAsync<string>(
                "SELECT observaciones FROM caja.turno_caja"));
            Assert.Equal(1L, await database.ScalarAsync<long>("""
                SELECT count(*) FROM information_schema.columns
                WHERE table_schema='caja' AND table_name='turno_caja' AND column_name='diferencia_total'
                """));
            Assert.Equal(1L, await database.ScalarAsync<long>("""
                SELECT count(*) FROM pg_constraint
                WHERE conname='ck_turno_caja_diferencia_total'
                  AND conrelid='caja.turno_caja'::regclass
                """));
            Assert.Equal(1L, await database.ScalarAsync<long>("""
                SELECT count(*) FROM pg_indexes
                WHERE schemaname='ventas' AND indexname='ix_pago_venta_movimiento_caja'
                """));
            Assert.Equal(1L, await database.ScalarAsync<long>($"""
                SELECT count(*) FROM "__EFMigrationsHistory"
                WHERE "MigrationId"='{CashMigrationDatabase.CashMigration}'
                """));
            Assert.Equal(3L, await database.ScalarAsync<long>(
                "SELECT count(*) FROM catalogo.metodo_pago"));
        }
        finally
        {
            if (advisoryLockHeld)
            {
                try { await ReleaseAdvisoryLockAsync(coordinator); }
                catch (Exception) { }
            }
            if (migrationTask is not null)
            {
                try { await migrationTask; }
                catch (Exception) { }
            }
            if (insertTask is not null)
            {
                try { await insertTask; }
                catch (Exception) { }
            }
            try { await DropTestEventTriggerAsync(database.Connection); }
            catch (Exception) { }
        }
    }

    private static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var context = new ControlPlusDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync(
            CashMigrationDatabase.CashMigration, cancellationToken);
    }

    private static async Task<NpgsqlConnection> OpenNamedConnectionAsync(
        CashMigrationDatabase database,
        string applicationName)
    {
        var connection = new NpgsqlConnection(NamedConnectionString(database, applicationName));
        await connection.OpenAsync();
        return connection;
    }

    private static string NamedConnectionString(CashMigrationDatabase database, string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            ApplicationName = applicationName
        };
        return builder.ConnectionString;
    }

    private static async Task WaitForRelationLockAsync(
        NpgsqlConnection observer,
        string applicationName,
        string mode,
        bool granted,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                  SELECT 1
                  FROM pg_locks locks
                  JOIN pg_stat_activity activity ON activity.pid = locks.pid
                  WHERE activity.application_name = @application_name
                    AND locks.locktype = 'relation'
                    AND locks.relation = 'caja.turno_caja'::regclass
                    AND locks.mode = @mode
                    AND locks.granted = @granted);
                """, observer);
            command.Parameters.AddWithValue("application_name", applicationName);
            command.Parameters.AddWithValue("mode", mode);
            command.Parameters.AddWithValue("granted", granted);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!) return;
            await Task.Delay(20, cancellationToken);
        }
    }

    private static async Task<bool> ReleaseAdvisoryLockAsync(NpgsqlConnection coordinator)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT pg_advisory_unlock({MigrationGate})", coordinator);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static Task DropTestEventTriggerAsync(NpgsqlConnection connection) =>
        CashMigrationDatabase.ExecuteAsync(connection, """
            DROP EVENT TRIGGER IF EXISTS test_pause_cash_migration_ddl;
            DROP FUNCTION IF EXISTS public.test_pause_cash_migration_ddl();
            """);
}
