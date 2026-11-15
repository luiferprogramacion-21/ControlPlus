using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class ConfigurableWholesaleMinimumMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261115010000_OptionalSalePaymentReferenceV1";
    private const string Target = "20261115020000_ConfigurableWholesaleMinimumV1";
    private const string Identification = "WHOLESALE-MINIMUM-MIGRATION";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_KeepsTwelveForExistingEstablishments_AndLimitsTheRange()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(24, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"""
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES ('{Guid.CreateVersion7()}','Mayorista prueba','{Identification}');
            """);

        await migrator.MigrateAsync(Target);

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(25, applied.Length);
        Assert.Equal(Target, applied[^1]);
        // Un establecimiento existente queda con la regla fija anterior.
        Assert.Equal(12, await MinimumAsync(connection));

        await SetMinimumAsync(connection, 1);
        Assert.Equal(1, await MinimumAsync(connection));
        await SetMinimumAsync(connection, 999);
        Assert.Equal(999, await MinimumAsync(connection));
        await Assert.ThrowsAsync<PostgresException>(() => SetMinimumAsync(connection, 0));
        await Assert.ThrowsAsync<PostgresException>(() => SetMinimumAsync(connection, 1000));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
            UPDATE configuracion.establecimiento SET minimo_unidades_mayorista = NULL
            WHERE identificacion = '{Identification}';
            """));
    }

    private static async Task<int> MinimumAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT minimo_unidades_mayorista FROM configuracion.establecimiento
            WHERE identificacion = '{Identification}'
            """, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SetMinimumAsync(NpgsqlConnection connection, int value)
    {
        await using var command = new NpgsqlCommand($"""
            UPDATE configuracion.establecimiento SET minimo_unidades_mayorista = @value
            WHERE identificacion = '{Identification}'
            """, connection);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
