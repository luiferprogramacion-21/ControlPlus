using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class OptionalEstablishmentIdentificationMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261119013000_HeldSalesV1";
    private const string Target = "20261119020000_OptionalEstablishmentIdentificationV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_AllowsNullButStillRejectsBlankIdentification()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(29, (await db.Database.GetAppliedMigrationsAsync()).Count());

        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"""
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES ('{firstId}','Negocio con NIT','NIT-MIGRATION');
            """);

        await migrator.MigrateAsync(Target);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(30, applied.Length);
        Assert.Equal(Target, applied[^1]);
        Assert.Equal("YES", await ScalarAsync(connection, """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_schema='configuracion' AND table_name='establecimiento'
              AND column_name='identificacion'
            """));

        await ExecuteAsync(connection, $"""
            UPDATE configuracion.establecimiento SET identificacion = NULL WHERE id = '{firstId}';
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES ('{secondId}','Otro negocio sin NIT',NULL);
            """);
        Assert.Equal(2L, await ScalarAsync(connection,
            "SELECT count(*) FROM configuracion.establecimiento WHERE identificacion IS NULL"));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            UPDATE configuracion.establecimiento SET identificacion = ' ' WHERE id = '{firstId}'
            """));

        await ExecuteAsync(connection, $"""
            UPDATE configuracion.establecimiento SET identificacion = 'NIT-{firstId.ToString("N")[..20]}' WHERE id = '{firstId}';
            UPDATE configuracion.establecimiento SET identificacion = 'NIT-{secondId.ToString("N")[..20]}' WHERE id = '{secondId}';
            """);
        await migrator.MigrateAsync(Previous);
        Assert.Equal("NO", await ScalarAsync(connection, """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_schema='configuracion' AND table_name='establecimiento'
              AND column_name='identificacion'
            """));
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSqlStateAsync(string expected, Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(operation);
        Assert.Equal(expected, error.SqlState);
    }
}
