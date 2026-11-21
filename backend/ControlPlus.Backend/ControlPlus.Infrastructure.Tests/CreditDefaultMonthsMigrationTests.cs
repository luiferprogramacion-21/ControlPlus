using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class CreditDefaultMonthsMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261121010000_OptionalCreditLayawayPaymentReferenceV1";
    private const string Target = "20261121020000_CreditDefaultMonthsV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_PreservesDays_AndEnforcesExclusiveCalendarMonths()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(36, (await db.Database.GetAppliedMigrationsAsync()).Count());

        var establishmentId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES (@id,'Crédito por meses','CREDIT-MONTHS-MIGRATION');
            """, establishmentId);

        await migrator.MigrateAsync(Target);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(37, applied.Length);
        Assert.Equal(Target, applied[^1]);
        Assert.Equal(((int?)30, (int?)null), await TermAsync(connection, establishmentId));

        await ExecuteAsync(connection, """
            UPDATE configuracion.establecimiento
            SET dias_credito_predeterminado = NULL, meses_credito_predeterminado = 3
            WHERE id = @id;
            """, establishmentId);
        Assert.Equal(((int?)null, (int?)3), await TermAsync(connection, establishmentId));

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, """
            UPDATE configuracion.establecimiento SET dias_credito_predeterminado = 30 WHERE id = @id;
            """, establishmentId));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, """
            UPDATE configuracion.establecimiento SET meses_credito_predeterminado = 4 WHERE id = @id;
            """, establishmentId));

        await ExecuteAsync(connection, """
            UPDATE configuracion.establecimiento SET meses_credito_predeterminado = NULL WHERE id = @id;
            """, establishmentId);
        Assert.Equal(((int?)null, (int?)null), await TermAsync(connection, establishmentId));
    }

    private static async Task<(int? Days, int? Months)> TermAsync(NpgsqlConnection connection, Guid id)
    {
        await using var command = new NpgsqlCommand("""
            SELECT dias_credito_predeterminado, meses_credito_predeterminado
            FROM configuracion.establecimiento WHERE id = @id;
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
