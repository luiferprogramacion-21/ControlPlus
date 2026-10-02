using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static class InventoryDatabaseValidation
{
    public static async Task<bool> HasRequiredTableShapeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var actualColumnCount = await ScalarInt64Async(
            connection,
            transaction,
            """
            SELECT count(*)
            FROM information_schema.columns
            WHERE table_schema = @schema
              AND table_name = @table
            """,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("schema", InventoryMigrationManifest.Schema);
                command.Parameters.AddWithValue("table", InventoryMigrationManifest.Table);
            });
        if (actualColumnCount != InventoryMigrationManifest.RequiredColumns.Count)
        {
            return false;
        }

        foreach (var required in InventoryMigrationManifest.RequiredColumns)
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
            command.Parameters.AddWithValue("schema", InventoryMigrationManifest.Schema);
            command.Parameters.AddWithValue("table", InventoryMigrationManifest.Table);
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

    public static async Task<bool> HasRequiredNaturalKeyConstraintAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var required = InventoryMigrationManifest.RequiredNaturalKeyConstraint;
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT constraint_definition.contype::text,
                                constraint_definition.convalidated
                         FROM pg_catalog.pg_constraint AS constraint_definition
                         JOIN pg_catalog.pg_class AS relation
                           ON relation.oid = constraint_definition.conrelid
                         JOIN pg_catalog.pg_namespace AS namespace
                           ON namespace.oid = relation.relnamespace
                         WHERE namespace.nspname = @schema
                           AND relation.relname = @table
                           AND constraint_definition.conname = @name
                         """,
                         connection,
                         transaction))
        {
            command.Parameters.AddWithValue("schema", required.Schema);
            command.Parameters.AddWithValue("table", required.Table);
            command.Parameters.AddWithValue("name", required.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                !string.Equals(reader.GetString(0), "u", StringComparison.Ordinal) ||
                reader.GetBoolean(1) != required.MustBeValidated ||
                await reader.ReadAsync(cancellationToken))
            {
                return false;
            }
        }

        var actualColumns = new List<string>();
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT attribute.attname
                         FROM pg_catalog.pg_constraint AS constraint_definition
                         JOIN pg_catalog.pg_class AS relation
                           ON relation.oid = constraint_definition.conrelid
                         JOIN pg_catalog.pg_namespace AS namespace
                           ON namespace.oid = relation.relnamespace
                         CROSS JOIN LATERAL unnest(constraint_definition.conkey)
                           WITH ORDINALITY AS key_column(attribute_number, position)
                         JOIN pg_catalog.pg_attribute AS attribute
                           ON attribute.attrelid = relation.oid
                          AND attribute.attnum = key_column.attribute_number
                         WHERE namespace.nspname = @schema
                           AND relation.relname = @table
                           AND constraint_definition.conname = @name
                         ORDER BY key_column.position
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

        return actualColumns.SequenceEqual(required.Columns, StringComparer.Ordinal);
    }

    public static async Task<long> CountEstablishmentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken) =>
        await ScalarInt64Async(
            connection,
            transaction,
            "SELECT count(*) FROM configuracion.establecimiento",
            cancellationToken);

    public static async Task<InventoryPreexistingReasonCounts> CountPreexistingReasonsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var perCode = InventoryMigrationManifest.Reasons.ToDictionary(
            reason => reason.Code,
            _ => 0L,
            StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT codigo, count(*)
                         FROM configuracion.motivo_operacion
                         WHERE tipo_operacion = @operation_type
                           AND codigo = ANY(@codes)
                         GROUP BY codigo
                         """,
                         connection,
                         transaction))
        {
            AddReasonParameters(command);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                perCode[reader.GetString(0)] = reader.GetInt64(1);
            }
        }

        var wrongNaturalKeyIds = await ScalarInt64Async(
            connection,
            transaction,
            """
            SELECT count(*)
            FROM configuracion.motivo_operacion AS reason
            WHERE reason.tipo_operacion = @operation_type
              AND reason.codigo = ANY(@codes)
              AND reason.id <> md5(
                    @id_prefix || reason.establecimiento_id::text || '.' || reason.codigo)::uuid
            """,
            cancellationToken,
            AddReasonParameters);

        var deterministicIdCollisions = await ScalarInt64Async(
            connection,
            transaction,
            """
            SELECT count(*)
            FROM configuracion.establecimiento AS establishment
            CROSS JOIN unnest(@codes::text[]) AS expected(code)
            JOIN configuracion.motivo_operacion AS reason
              ON reason.id = md5(
                    @id_prefix || establishment.id::text || '.' || expected.code)::uuid
            WHERE reason.establecimiento_id <> establishment.id
               OR reason.tipo_operacion <> @operation_type
               OR reason.codigo <> expected.code
            """,
            cancellationToken,
            AddReasonParameters);

        return new InventoryPreexistingReasonCounts(
            perCode.Values.Sum(),
            wrongNaturalKeyIds,
            deterministicIdCollisions,
            perCode);
    }

    public static async Task<InventoryPostflightCounts> CountPostflightStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var approvedRows = await ScalarInt64Async(
            connection,
            transaction,
            """
            SELECT count(*)
            FROM configuracion.motivo_operacion
            WHERE tipo_operacion = @operation_type
              AND codigo = ANY(@codes)
            """,
            cancellationToken,
            AddReasonParameters);

        var invalidExpectedRows = await ScalarInt64Async(
            connection,
            transaction,
            """
            WITH expected(code, display_order, name, description) AS (
                VALUES
                    (@code0, @order0, @name0, @description0),
                    (@code1, @order1, @name1, @description1),
                    (@code2, @order2, @name2, @description2),
                    (@code3, @order3, @name3, @description3)
            )
            SELECT count(*)
            FROM configuracion.establecimiento AS establishment
            CROSS JOIN expected
            LEFT JOIN configuracion.motivo_operacion AS reason
              ON reason.establecimiento_id = establishment.id
             AND reason.tipo_operacion = @operation_type
             AND reason.codigo = expected.code
            WHERE reason.id IS NULL
               OR reason.id <> md5(
                    @id_prefix || establishment.id::text || '.' || expected.code)::uuid
               OR reason.nombre <> expected.name
               OR reason.descripcion IS DISTINCT FROM expected.description
               OR reason.orden_visual <> expected.display_order
               OR reason.activo IS NOT TRUE
            """,
            cancellationToken,
            command =>
            {
                AddReasonParameters(command);
                for (var index = 0; index < InventoryMigrationManifest.Reasons.Count; index++)
                {
                    var reason = InventoryMigrationManifest.Reasons[index];
                    command.Parameters.AddWithValue($"code{index}", reason.Code);
                    command.Parameters.AddWithValue($"order{index}", reason.DisplayOrder);
                    command.Parameters.AddWithValue($"name{index}", reason.Name);
                    command.Parameters.AddWithValue($"description{index}", reason.Description);
                }
            });

        return new InventoryPostflightCounts(approvedRows, invalidExpectedRows);
    }

    public static async Task<InventoryUnrelatedReasonsFingerprint>
        CalculateUnrelatedReasonsFingerprintAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id,
                   establecimiento_id,
                   tipo_operacion,
                   codigo,
                   nombre,
                   descripcion,
                   orden_visual,
                   activo,
                   fecha_creacion,
                   fecha_modificacion,
                   version
            FROM configuracion.motivo_operacion
            WHERE NOT (
                tipo_operacion = @operation_type
                AND codigo = ANY(@codes))
            ORDER BY id
            """,
            connection,
            transaction);
        AddReasonParameters(command);

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long count = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AppendByte(sha256, 0x1e);
            AppendString(
                sha256,
                reader.GetGuid(0).ToString("D", CultureInfo.InvariantCulture));
            AppendString(
                sha256,
                reader.GetGuid(1).ToString("D", CultureInfo.InvariantCulture));
            AppendString(sha256, reader.GetString(2));
            AppendString(sha256, reader.GetString(3));
            AppendString(sha256, reader.GetString(4));
            AppendNullableString(sha256, reader.IsDBNull(5) ? null : reader.GetString(5));
            AppendInt32(sha256, reader.GetInt32(6));
            AppendByte(sha256, reader.GetBoolean(7) ? (byte)1 : (byte)0);
            AppendInt64(sha256, reader.GetDateTime(8).ToUniversalTime().Ticks);
            AppendNullableInt64(
                sha256,
                reader.IsDBNull(9)
                    ? null
                    : reader.GetDateTime(9).ToUniversalTime().Ticks);
            AppendInt64(sha256, reader.GetInt64(10));
            count++;
        }

        return new InventoryUnrelatedReasonsFingerprint(
            count,
            Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant());
    }

    public static async Task<bool> IsTransactionReadOnlyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SHOW transaction_read_only", connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return string.Equals(value as string, "on", StringComparison.Ordinal);
    }

    public static async Task SetTransactionReadOnlyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddReasonParameters(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("operation_type", InventoryMigrationManifest.OperationType);
        command.Parameters.AddWithValue(
            "codes",
            InventoryMigrationManifest.Reasons.Select(reason => reason.Code).ToArray());
        command.Parameters.AddWithValue("id_prefix", InventoryMigrationManifest.DeterministicIdPrefix);
    }

    private static void AppendNullableString(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            AppendByte(hash, 0);
            return;
        }

        AppendByte(hash, 1);
        AppendString(hash, value);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendNullableInt64(IncrementalHash hash, long? value)
    {
        if (value is null)
        {
            AppendByte(hash, 0);
            return;
        }

        AppendByte(hash, 1);
        AppendInt64(hash, value.Value);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendByte(IncrementalHash hash, byte value) =>
        hash.AppendData([value]);

    private static async Task<long> ScalarInt64Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        Action<NpgsqlCommand>? configure = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        configure?.Invoke(command);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}

internal sealed record InventoryPreexistingReasonCounts(
    long ApprovedRows,
    long WrongNaturalKeyIds,
    long DeterministicIdCollisions,
    IReadOnlyDictionary<string, long> PerCode);

internal sealed record InventoryPostflightCounts(
    long ApprovedRows,
    long InvalidExpectedRows);

internal sealed record InventoryUnrelatedReasonsFingerprint(
    long Count,
    string Sha256);
