using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class BusinessVatWholePesoLayawaysChangesMigrationTests : IAsyncLifetime
{
    private readonly LocalPostgresDatabase _postgres = new();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory, Trait("Category", "Integration")]
    [InlineData("detalle_apartado", "ck_detalle_apartado_iva_incluido")]
    [InlineData("detalle_cambio_venta", "ck_detalle_cambio_iva_incluido")]
    public async Task Check_PreservesHistoricalAndAcceptsWholePeso(string table, string check)
    {
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261110020000_BusinessVatWholePesoSalesV1");
        await db.GetService<IMigrator>().MigrateAsync("20261111010000_BusinessVatWholePesoLayawaysChangesV1");
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var definitionCommand = new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = @table::regclass AND conname = @check", connection, transaction);
        definitionCommand.Parameters.AddWithValue("table", $"ventas.{table}");
        definitionCommand.Parameters.AddWithValue("check", check);
        var definition = (string?)await definitionCommand.ExecuteScalarAsync();
        Assert.NotNull(definition);
        Assert.Contains("round", definition);
        await using (var create = new NpgsqlCommand($"CREATE TEMP TABLE vat_check (subtotal_neto numeric(18,4), porcentaje_iva numeric(5,2), base_gravable numeric(18,4), valor_iva numeric(18,4), CONSTRAINT {check} {definition})", connection, transaction))
            await create.ExecuteNonQueryAsync();
        foreach (var (basis, tax) in new[] { (841.1765m, 159.8235m), (841m, 160m) })
        {
            await using var insert = new NpgsqlCommand("INSERT INTO vat_check VALUES (1001,19,@basis,@tax)", connection, transaction);
            insert.Parameters.AddWithValue("basis", basis);
            insert.Parameters.AddWithValue("tax", tax);
            await insert.ExecuteNonQueryAsync();
        }
        await using (var bad = new NpgsqlCommand("SAVEPOINT invalid_row", connection, transaction))
            await bad.ExecuteNonQueryAsync();
        await using (var insert = new NpgsqlCommand("INSERT INTO vat_check VALUES (1001,19,841,159)", connection, transaction))
            await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        await using (var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT invalid_row", connection, transaction))
            await rollback.ExecuteNonQueryAsync();
        await transaction.RollbackAsync();
    }
}
