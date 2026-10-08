using System.Security.Cryptography;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class CustomerSearchIndexMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260930010000_SeedInventoryAdjustmentReasonsV1";
    private const string TargetMigration = "20261005010000_CustomerSearchIndexesV1";
    private static readonly IReadOnlyDictionary<string, string> ExpectedIndexes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ix_cliente_busqueda_nombre_prefijo"] =
                "CREATE INDEX ix_cliente_busqueda_nombre_prefijo ON catalogo.cliente USING btree (lower((nombre_completo)::text) text_pattern_ops)",
            ["ix_cliente_busqueda_documento_prefijo"] =
                "CREATE INDEX ix_cliente_busqueda_documento_prefijo ON catalogo.cliente USING btree (numero_documento varchar_pattern_ops) WHERE (numero_documento IS NOT NULL)",
            ["ix_cliente_busqueda_telefono_prefijo"] =
                "CREATE INDEX ix_cliente_busqueda_telefono_prefijo ON catalogo.cliente USING btree (lower((telefono)::text) text_pattern_ops) WHERE (telefono IS NOT NULL)",
            ["ix_cliente_activo_nombre_id"] =
                "CREATE INDEX ix_cliente_activo_nombre_id ON catalogo.cliente USING btree (activo, nombre_completo, id)"
        };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("controlplus_customer_search_migration_test")
        .WithUsername("controlplus_test")
        .WithPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task UpDownUp_PreservesAllCustomersAndDefinesExactPrefixIndexesWithEligiblePlans()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await SeedCustomersAsync();
        var customersBefore = await CustomerSnapshotAsync();
        var indexesBefore = await AllCustomerIndexSnapshotAsync();

        await migrator.MigrateAsync(TargetMigration);

        Assert.Equal(ExpectedIndexes, await TargetIndexDefinitionsAsync());
        Assert.Equal(customersBefore, await CustomerSnapshotAsync());
        Assert.Contains("ix_cliente_busqueda_nombre_prefijo", await ExplainAsync(
            "SELECT id FROM catalogo.cliente WHERE lower(nombre_completo) LIKE @pattern ESCAPE '\\'",
            "prefixname%"), StringComparison.Ordinal);
        Assert.Contains("ix_cliente_busqueda_documento_prefijo", await ExplainAsync(
            "SELECT id FROM catalogo.cliente WHERE numero_documento LIKE @pattern ESCAPE '\\'",
            "DOC-01%"), StringComparison.Ordinal);
        Assert.Contains("ix_cliente_busqueda_telefono_prefijo", await ExplainAsync(
            "SELECT id FROM catalogo.cliente WHERE lower(telefono) LIKE @pattern ESCAPE '\\'",
            "555-01%"), StringComparison.Ordinal);
        var activeListPlan = await ExplainActiveListAsync(page: 3, pageSize: 20);
        Assert.Contains("Index Scan using ix_cliente_activo_nombre_id", activeListPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("Sort", activeListPlan, StringComparison.Ordinal);
        Assert.Equal(TargetMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());

        await migrator.MigrateAsync(PreviousMigration);

        Assert.Empty(await TargetIndexDefinitionsAsync());
        Assert.Equal(indexesBefore, await AllCustomerIndexSnapshotAsync());
        Assert.Equal(customersBefore, await CustomerSnapshotAsync());
        Assert.Equal(400L, await ScalarAsync("SELECT count(*) FROM catalogo.cliente"));

        await migrator.MigrateAsync(TargetMigration);

        Assert.Equal(ExpectedIndexes, await TargetIndexDefinitionsAsync());
        Assert.Equal(customersBefore, await CustomerSnapshotAsync());
        Assert.Equal(400L, await ScalarAsync("SELECT count(*) FROM catalogo.cliente"));
    }

    private async Task SeedCustomersAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO seguridad.usuario(
                id, nombre_usuario, nombre_usuario_normalizado, nombre_completo,
                password_hash, sello_seguridad, sello_concurrencia)
            VALUES (
                '019a0000-0000-7000-8000-000000000001',
                'customer-index-test', 'CUSTOMER-INDEX-TEST', 'Customer index test',
                'unused-test-fixture', 'test', 'test');

            INSERT INTO catalogo.cliente(
                id, tipo_documento, numero_documento, nombre_completo, telefono,
                direccion, correo, observaciones, activo, usuario_creacion_id,
                fecha_creacion, version)
            SELECT
                ('019a0000-0000-7000-8000-' || lpad(number::text, 12, '0'))::uuid,
                'CC',
                'DOC-' || lpad(number::text, 4, '0'),
                'PrefixName ' || lpad(number::text, 4, '0'),
                '555-' || lpad(number::text, 4, '0'),
                'Address ' || number::text,
                'customer' || number::text || '@example.test',
                'Fixture ' || number::text,
                number % 5 <> 0,
                '019a0000-0000-7000-8000-000000000001',
                CURRENT_TIMESTAMP,
                1
            FROM generate_series(1, 400) AS number;
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyDictionary<string, string>> TargetIndexDefinitionsAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT indexname, indexdef
            FROM pg_catalog.pg_indexes
            WHERE schemaname='catalogo'
              AND tablename='cliente'
              AND indexname = ANY(@names)
            ORDER BY indexname
            """,
            connection);
        command.Parameters.AddWithValue("names", ExpectedIndexes.Keys.ToArray());
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            definitions.Add(reader.GetString(0), reader.GetString(1));
        return definitions;
    }

    private async Task<string> ExplainAsync(string query, string pattern)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var settings = new NpgsqlCommand(
                         "SET enable_seqscan=off; SET enable_bitmapscan=off",
                         connection))
            await settings.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand($"EXPLAIN (COSTS OFF) {query}", connection);
        command.Parameters.AddWithValue("pattern", pattern);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private async Task<string> ExplainActiveListAsync(int page, int pageSize)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var settings = new NpgsqlCommand(
                         "SET enable_seqscan=off; SET enable_bitmapscan=off",
                         connection))
            await settings.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(
            """
            EXPLAIN (COSTS OFF)
            SELECT
                id, tipo_documento, numero_documento, nombre_completo, telefono,
                direccion, correo, observaciones, activo, usuario_creacion_id,
                usuario_modificacion_id, fecha_creacion, fecha_modificacion, version
            FROM catalogo.cliente
            WHERE activo
            ORDER BY nombre_completo, id
            LIMIT @page_size OFFSET @offset
            """,
            connection);
        command.Parameters.AddWithValue("page_size", pageSize);
        command.Parameters.AddWithValue("offset", (page - 1) * pageSize);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private async Task<string> CustomerSnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(string_agg(
                id::text || '|' || coalesce(tipo_documento, '') || '|' ||
                coalesce(numero_documento, '') || '|' || nombre_completo || '|' ||
                coalesce(telefono, '') || '|' || coalesce(direccion, '') || '|' ||
                coalesce(correo, '') || '|' || coalesce(observaciones, '') || '|' ||
                activo::text || '|' || usuario_creacion_id::text || '|' ||
                coalesce(usuario_modificacion_id::text, '') || '|' || version::text,
                E'\n' ORDER BY id), '')
            FROM catalogo.cliente
            """,
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> AllCustomerIndexSnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
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

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private ControlPlusDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
