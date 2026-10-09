using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static class CustomerSearchDatabaseValidation
{
    public static async Task<bool> HasRequiredTableShapeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var count = new NpgsqlCommand(
                         """
                         SELECT count(*)
                         FROM information_schema.columns
                         WHERE table_schema = @schema AND table_name = @table
                         """,
                         connection,
                         transaction))
        {
            AddTableParameters(count);
            if (Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken)) !=
                CustomerSearchMigrationManifest.RequiredColumns.Count)
            {
                return false;
            }
        }

        foreach (var required in CustomerSearchMigrationManifest.RequiredColumns)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT data_type, character_maximum_length, is_nullable
                FROM information_schema.columns
                WHERE table_schema = @schema
                  AND table_name = @table
                  AND column_name = @column
                """,
                connection,
                transaction);
            AddTableParameters(command);
            command.Parameters.AddWithValue("column", required.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                !string.Equals(reader.GetString(0), required.DataType, StringComparison.Ordinal) ||
                (reader.IsDBNull(1) ? null : reader.GetInt32(1)) != required.CharacterMaximumLength ||
                string.Equals(reader.GetString(2), "YES", StringComparison.Ordinal) != required.IsNullable ||
                await reader.ReadAsync(cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> ReservedIndexesAreAbsentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var index in CustomerSearchMigrationManifest.Indexes)
        {
            if (await IndexExistsAsync(connection, transaction, index.Name, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> IndexesMatchManifestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var expected in CustomerSearchMigrationManifest.Indexes)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT indexdef
                FROM pg_catalog.pg_indexes
                WHERE schemaname = @schema
                  AND tablename = @table
                  AND indexname = @name
                """,
                connection,
                transaction);
            AddTableParameters(command);
            command.Parameters.AddWithValue("name", expected.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                !string.Equals(reader.GetString(0), expected.Definition, StringComparison.Ordinal) ||
                await reader.ReadAsync(cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    public static async Task SetTransactionReadOnlyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<bool> IsTransactionReadOnlyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SHOW transaction_read_only", connection, transaction);
        return string.Equals(
            await command.ExecuteScalarAsync(cancellationToken) as string,
            "on",
            StringComparison.Ordinal);
    }

    private static async Task<bool> IndexExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_indexes
                WHERE schemaname = @schema
                  AND indexname = @name)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("schema", CustomerSearchMigrationManifest.Schema);
        command.Parameters.AddWithValue("name", name);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static void AddTableParameters(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("schema", CustomerSearchMigrationManifest.Schema);
        command.Parameters.AddWithValue("table", CustomerSearchMigrationManifest.Table);
    }
}
