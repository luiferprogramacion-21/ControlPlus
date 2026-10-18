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
        await using var blocker = await coordinator.BeginTransactionAsync();
        Task? migrationTask = null;
        Task<int>? insertTask = null;
        var blockerHeld = true;

        try
        {
            // The migration changes pago_venta only after checking and altering turno_caja.
            // A read lock there pauses its later DDL while it still owns the cash lock.
            await using (var hold = new NpgsqlCommand(
                "LOCK TABLE ventas.pago_venta IN ACCESS SHARE MODE", coordinator, blocker))
            {
                await hold.ExecuteNonQueryAsync(timeout.Token);
            }

            var migrationConnectionString = NamedConnectionString(database, MigrationApplication);
            migrationTask = MigrateAsync(migrationConnectionString, timeout.Token);
            await WaitForRelationLockAsync(observer, MigrationApplication,
                "caja.turno_caja", "AccessExclusiveLock", granted: true, timeout.Token);
            await WaitForRelationLockAsync(observer, MigrationApplication,
                "ventas.pago_venta", "AccessExclusiveLock", granted: false, timeout.Token);
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
                "caja.turno_caja", "RowExclusiveLock", granted: false, timeout.Token);
            Assert.False(insertTask.IsCompleted);

            await blocker.CommitAsync(timeout.Token);
            blockerHeld = false;
            await migrationTask;
            Assert.Equal(1, await insertTask);

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
            if (blockerHeld)
            {
                try { await blocker.RollbackAsync(); }
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
        string relation,
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
                    AND locks.relation = to_regclass(@relation)
                    AND locks.mode = @mode
                    AND locks.granted = @granted);
                """, observer);
            command.Parameters.AddWithValue("application_name", applicationName);
            command.Parameters.AddWithValue("relation", relation);
            command.Parameters.AddWithValue("mode", mode);
            command.Parameters.AddWithValue("granted", granted);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!) return;
            await Task.Delay(20, cancellationToken);
        }
    }
}
