using System.Data;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

public sealed record ControlledPreflightResult(int ExitCode, string Message);

public static class ControlledPreflightRunner
{
    public const int FailedExitCode = 1;
    public const int RejectedExitCode = 2;
    public const string CashMigration = CashRegisterPreflightManifest.TargetMigration;

    public static async Task<ControlledPreflightResult> RunAsync(
        IConfiguration configuration,
        string targetMigration,
        CancellationToken cancellationToken = default)
    {
        if (!ControlledExecutionCommand.IsValidMigrationId(targetMigration))
        {
            return Rejected("invalid-target", "the target identifier is invalid");
        }

        var safeTarget = targetMigration;

        if (!string.Equals(targetMigration, CashMigration, StringComparison.Ordinal))
        {
            return Rejected(safeTarget, "the target is not supported by this preflight");
        }

        var connectionString = configuration.GetConnectionString("ControlPlusDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Rejected(safeTarget, "the database configuration is unavailable");
        }

        try
        {
            var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var context = new ControlPlusDbContext(options);

            var catalog = context.Database.GetMigrations().ToArray();
            var targetIndex = Array.IndexOf(catalog, targetMigration);
            if (targetIndex != CashRegisterPreflightManifest.RequiredPreviousMigrations.Count ||
                !catalog.Take(targetIndex).SequenceEqual(
                    CashRegisterPreflightManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal))
            {
                return Rejected(safeTarget, "the EF migration catalog is not the required coherent sequence");
            }

            await context.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

            await ExecuteNonQueryAsync(
                connection,
                npgsqlTransaction,
                "SET TRANSACTION READ ONLY",
                cancellationToken);
            if (!await IsTransactionReadOnlyAsync(connection, npgsqlTransaction, cancellationToken))
            {
                return Rejected(safeTarget, "PostgreSQL did not confirm a read-only transaction");
            }

            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            if (applied.Contains(targetMigration, StringComparer.Ordinal))
            {
                return Rejected(safeTarget, "the target is already applied");
            }

            if (!applied.SequenceEqual(
                    CashRegisterPreflightManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal))
            {
                return Rejected(
                    safeTarget,
                    "the EF history must contain exactly the four required predecessor migrations");
            }

            if (!await IsTableEmptyAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return Rejected(safeTarget, "caja.turno_caja is not empty");
            }

            foreach (var function in CashRegisterPreflightManifest.CreatedFunctions)
            {
                if (await FunctionExistsAsync(
                        connection,
                        npgsqlTransaction,
                        function,
                        cancellationToken))
                {
                    return Rejected(safeTarget, "a function reserved for the target already exists");
                }
            }

            foreach (var trigger in CashRegisterPreflightManifest.CreatedTriggers)
            {
                if (await TriggerExistsAsync(
                        connection,
                        npgsqlTransaction,
                        trigger,
                        cancellationToken))
                {
                    return Rejected(safeTarget, "a trigger reserved for the target already exists");
                }
            }

            foreach (var index in CashRegisterPreflightManifest.CreatedIndexes)
            {
                if (await IndexExistsAsync(
                        connection,
                        npgsqlTransaction,
                        index,
                        cancellationToken))
                {
                    return Rejected(safeTarget, "an index reserved for the target already exists");
                }
            }

            foreach (var table in CashRegisterPreflightManifest.CreatedTables)
            {
                if (await TableExistsAsync(
                        connection,
                        npgsqlTransaction,
                        table,
                        cancellationToken))
                {
                    return Rejected(safeTarget, "a table reserved for the target already exists");
                }
            }

            if (await ColumnExistsAsync(
                    connection,
                    npgsqlTransaction,
                    CashRegisterPreflightManifest.CreatedDifferenceTotalColumn,
                    cancellationToken))
            {
                return Rejected(safeTarget, "a column reserved for the target already exists");
            }

            foreach (var constraint in CashRegisterPreflightManifest.RequiredBaseConstraints)
            {
                if (!await RequiredConstraintExistsAsync(
                        connection,
                        npgsqlTransaction,
                        constraint,
                        cancellationToken))
                {
                    return Rejected(
                        safeTarget,
                        "the seven required base constraints are not present exactly");
                }
            }

            if (!await IsTransactionReadOnlyAsync(connection, npgsqlTransaction, cancellationToken))
            {
                return Rejected(safeTarget, "the read-only transaction context was not preserved");
            }

            await transaction.RollbackAsync(cancellationToken);
            return new ControlledPreflightResult(
                0,
                $"Preflight target '{safeTarget}' certified in a read-only transaction.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlledPreflightResult(
                FailedExitCode,
                $"Preflight target '{safeTarget}' was cancelled.");
        }
        catch
        {
            // Deliberately omit provider details: they can contain SQL or connection data.
            return Failed(safeTarget);
        }
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(commandText, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsTransactionReadOnlyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SHOW transaction_read_only",
            connection,
            transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return string.Equals(value as string, "on", StringComparison.Ordinal);
    }

    private static async Task<bool> IsTableEmptyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT NOT EXISTS (SELECT 1 FROM caja.turno_caja)",
            connection,
            transaction);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreflightTableObject table,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @name
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("schema", table.Schema);
        command.Parameters.AddWithValue("name", table.Name);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreflightColumnObject column,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = @schema
                  AND table_name = @table
                  AND column_name = @column
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("schema", column.Schema);
        command.Parameters.AddWithValue("table", column.Table);
        command.Parameters.AddWithValue("column", column.Column);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> IndexExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreflightIndexObject index,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_indexes
                WHERE schemaname = @schema AND indexname = @name
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("schema", index.Schema);
        command.Parameters.AddWithValue("name", index.Name);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> TriggerExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreflightTriggerObject trigger,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_trigger AS trigger
                JOIN pg_catalog.pg_class AS relation ON relation.oid = trigger.tgrelid
                JOIN pg_catalog.pg_namespace AS namespace ON namespace.oid = relation.relnamespace
                WHERE NOT trigger.tgisinternal
                  AND namespace.nspname = @schema
                  AND relation.relname = @table
                  AND trigger.tgname = @name
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("schema", trigger.Schema);
        command.Parameters.AddWithValue("table", trigger.Table);
        command.Parameters.AddWithValue("name", trigger.Name);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> FunctionExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreflightFunctionObject function,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_proc AS procedure
                JOIN pg_catalog.pg_namespace AS namespace ON namespace.oid = procedure.pronamespace
                WHERE namespace.nspname = @schema
                  AND procedure.proname = @name
                  AND procedure.pronargs = @argument_count
                  AND pg_catalog.oidvectortypes(procedure.proargtypes) = @argument_types
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("schema", function.Schema);
        command.Parameters.AddWithValue("name", function.Name);
        command.Parameters.AddWithValue("argument_count", function.ArgumentTypes.Count);
        command.Parameters.AddWithValue("argument_types", string.Join(", ", function.ArgumentTypes));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> RequiredConstraintExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreflightRequiredConstraint required,
        CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT CASE base_constraint.contype
                                    WHEN 'c' THEN 1
                                    WHEN 'u' THEN 2
                                    ELSE 0
                                END,
                                base_constraint.convalidated
                         FROM pg_catalog.pg_constraint AS base_constraint
                         JOIN pg_catalog.pg_class AS relation ON relation.oid = base_constraint.conrelid
                         JOIN pg_catalog.pg_namespace AS namespace ON namespace.oid = relation.relnamespace
                         WHERE namespace.nspname = @schema
                           AND relation.relname = @table
                           AND base_constraint.conname = @name
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("schema", required.Schema);
            command.Parameters.AddWithValue("table", required.Table);
            command.Parameters.AddWithValue("name", required.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                reader.GetInt32(0) != (int)required.Kind ||
                reader.GetBoolean(1) != required.MustBeValidated ||
                await reader.ReadAsync(cancellationToken))
            {
                return false;
            }
        }

        var actualColumns = new List<string>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT column_name
                         FROM information_schema.constraint_column_usage
                         WHERE constraint_schema = @schema
                           AND table_schema = @schema
                           AND table_name = @table
                           AND constraint_name = @name
                         ORDER BY column_name
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("schema", required.Schema);
            command.Parameters.AddWithValue("table", required.Table);
            command.Parameters.AddWithValue("name", required.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                actualColumns.Add(reader.GetString(0));
            }
        }

        return actualColumns.SequenceEqual(
            required.Columns.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static ControlledPreflightResult Rejected(string target, string reason) =>
        new(RejectedExitCode, $"Preflight target '{target}' rejected: {reason}.");

    private static ControlledPreflightResult Failed(string target) =>
        new(FailedExitCode, $"Preflight target '{target}' failed without changing the database.");

}
