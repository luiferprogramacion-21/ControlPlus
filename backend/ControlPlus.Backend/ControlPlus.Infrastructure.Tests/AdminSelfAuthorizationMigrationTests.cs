using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class AdminSelfAuthorizationMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261102020000_CategoryProductReferencesV1";
    private const string Sale = "20261104010000_AdminSelfSaleCancellationV1";
    private const string Cash = "20261104020000_AdminSelfCashDifferenceV1";
    private const string SupervisorRefund = "20261105010000_SupervisorLayawayRefundDecisionV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task PreflightAndPostflight_ValidateEachCompensatingMigration_OnDisposableDatabase()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        await AssertScriptAsync("SalePreflight.sql", "SALE_SELF_PREFLIGHT_OK");
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());

        await migrator.MigrateAsync(Sale);
        await AssertScriptAsync("SalePostflight.sql", "SALE_SELF_POSTFLIGHT_OK");
        await AssertScriptAsync("CashPreflight.sql", "CASH_SELF_PREFLIGHT_OK");
        Assert.Equal(Sale, (await db.Database.GetAppliedMigrationsAsync()).Last());

        await migrator.MigrateAsync(Cash);
        await AssertScriptAsync("CashPostflight.sql", "CASH_SELF_POSTFLIGHT_OK");
        Assert.Equal(Cash, (await db.Database.GetAppliedMigrationsAsync()).Last());

        await AssertScriptAsync("SupervisorRefundPreflight.sql", "SUPERVISOR_REFUND_PREFLIGHT_OK");
        await migrator.MigrateAsync(SupervisorRefund);
        await AssertScriptAsync("SupervisorRefundPostflight.sql", "SUPERVISOR_REFUND_POSTFLIGHT_OK");
        Assert.Equal(SupervisorRefund, (await db.Database.GetAppliedMigrationsAsync()).Last());
    }

    private async Task AssertScriptAsync(string fileName, string expectedStatus)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "admin-self-authorization", fileName);
        var sql = await File.ReadAllTextAsync(path);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        Assert.Equal(expectedStatus, await command.ExecuteScalarAsync());
    }
}
