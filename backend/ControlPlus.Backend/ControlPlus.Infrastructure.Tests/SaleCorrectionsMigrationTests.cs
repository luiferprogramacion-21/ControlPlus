using System.IO.Compression;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ControlPlus.Tests;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class SaleCorrectionsMigrationTests
{
    [Fact]
    public void Script_OnlyRelaxesReplacementRequirement_AndKeepsAllOtherOfficialSettlementChecks()
    {
        using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=controlplus_test_script;Username=script_generation").Options);
        var sql = db.GetService<IMigrator>().GenerateScript("20261119010000_CashDrawerV1", "20261119020000_SaleCorrectionsV1");
        using var resource = typeof(ControlPlusDbContext).Assembly.GetManifestResourceStream(
            "ControlPlus.Infrastructure.Database.ControlPlus_Fase4_Documento_y_Complementos.zip")!;
        using var zip = new ZipArchive(resource);
        using var reader = new StreamReader(zip.Entries.Single(x => x.FullName.EndsWith("Script_Base_de_Datos_PostgreSQL_V3.sql", StringComparison.Ordinal)).Open());
        var official = reader.ReadToEnd().Replace("\r\n", "\n");
        var start = official.IndexOf("CREATE FUNCTION ventas.validar_liquidacion_cambio()", StringComparison.Ordinal);
        var end = official.IndexOf("$funcion$;", start, StringComparison.Ordinal) + "$funcion$;".Length;
        var expected = official[start..end].Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.Ordinal)
            .Replace("           OR v_lineas_entregadas = 0\n", "", StringComparison.Ordinal);
        Assert.Contains(expected, sql.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("OR v_lineas_entregadas = 0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(
            "20261119020000_SaleCorrectionsV1", "20261119010000_CashDrawerV1"));
    }
}

public sealed class SaleCorrectionsMigrationDatabaseTests : IAsyncLifetime
{
    private readonly LocalPostgresDatabase postgres = new();
    public Task InitializeAsync() => postgres.StartAsync();
    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_ChangesOnlyReplacementGuardInExistingFunction()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261119010000_CashDrawerV1");
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString()); await connection.OpenAsync();
        async Task<string> Body()
        {
            await using var query = new NpgsqlCommand("SELECT prosrc FROM pg_proc WHERE oid='ventas.validar_liquidacion_cambio()'::regprocedure", connection);
            return ((string)(await query.ExecuteScalarAsync())!).Replace("\r\n", "\n");
        }
        var before = await Body(); Assert.Contains("OR v_lineas_entregadas = 0", before);
        await migrator.MigrateAsync("20261119020000_SaleCorrectionsV1");
        Assert.Equal(before.Replace("           OR v_lineas_entregadas = 0\n", "", StringComparison.Ordinal), await Body());
        Assert.Equal(29, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal("20261119020000_SaleCorrectionsV1", (await db.Database.GetAppliedMigrationsAsync()).Last());
    }
}
