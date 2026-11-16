using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class CreditInstallmentsMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261116010000_CreditRiskControlsV1";
    private const string Target = "20261116020000_CreditInstallmentsV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_FollowsRiskControls_AndCreatesProtectedScheduleTables()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(26, (await db.Database.GetAppliedMigrationsAsync()).Count());

        await migrator.MigrateAsync(Target);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(27, applied.Length);
        Assert.Equal(Target, applied[^1]);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'ventas'
              AND table_name IN ('cuota_credito','aplicacion_pago_cuota')
            """, connection);
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);

        await using var checks = new NpgsqlCommand("""
            SELECT count(*) FROM pg_constraint
            WHERE conname IN ('ck_cuota_credito_numero','ck_cuota_credito_montos',
                              'uq_cuota_credito_numero','ck_aplicacion_pago_cuota_valor',
                              'uq_aplicacion_pago_cuota')
            """, connection);
        Assert.Equal(5L, (long)(await checks.ExecuteScalarAsync())!);
    }
}
