using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class ProductCodeAccessMigrationTests
{
    [Fact]
    public void Script_GrantsOnlyRequiredPrivilegesAndGuardsAbsentRole()
    {
        using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=controlplus_test_script;Username=script_generation").Options);
        var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript("20261029010000_PermanentProductReferencesV1", "20261102010000_GrantProductCodeAccessV1");
        Assert.Contains("pg_roles WHERE rolname = 'controlplus_app'", up);
        Assert.Contains("GRANT SELECT, INSERT ON TABLE catalogo.producto_codigo", up);
        Assert.Contains("GRANT USAGE ON SEQUENCE catalogo.producto_referencia_seq", up);
        Assert.Contains("GRANT EXECUTE ON FUNCTION catalogo.siguiente_referencia_producto()", up);
        Assert.Contains("GRANT EXECUTE ON FUNCTION catalogo.reservar_codigo_producto(text, uuid)", up);
        Assert.DoesNotContain("GRANT UPDATE", up);
        Assert.DoesNotContain("GRANT DELETE", up);
        Assert.DoesNotContain("CREATE TABLE", up);
        Assert.DoesNotContain("UPDATE catalogo.producto", up);
        var down = migrator.GenerateScript("20261102010000_GrantProductCodeAccessV1", "20261029010000_PermanentProductReferencesV1");
        Assert.Contains("REVOKE SELECT, INSERT ON TABLE catalogo.producto_codigo", down);
        Assert.Contains("REVOKE USAGE ON SEQUENCE catalogo.producto_referencia_seq", down);
    }
}

public sealed class ProductCodeAccessDatabaseTests : IAsyncLifetime
{
    private readonly LocalPostgresDatabase _postgres = new();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Upgrade_AppliesOnDisposableDatabase_WithOrWithoutApplicationRole()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261029010000_PermanentProductReferencesV1");
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var before = new NpgsqlCommand("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app')
                 THEN has_table_privilege('controlplus_app','catalogo.producto_codigo','SELECT')
                 ELSE NULL END
            """, connection);
        var hadSelect = await before.ExecuteScalarAsync();
        await migrator.MigrateAsync("20261102010000_GrantProductCodeAccessV1");
        Assert.Equal("20261102010000_GrantProductCodeAccessV1", (await db.Database.GetAppliedMigrationsAsync()).Last());
        if (hadSelect is bool)
        {
            await using var after = new NpgsqlCommand("""
                SELECT has_table_privilege('controlplus_app','catalogo.producto_codigo','SELECT')
                   AND has_table_privilege('controlplus_app','catalogo.producto_codigo','INSERT')
                   AND has_sequence_privilege('controlplus_app','catalogo.producto_referencia_seq','USAGE')
                   AND has_function_privilege('controlplus_app','catalogo.siguiente_referencia_producto()','EXECUTE')
                   AND has_function_privilege('controlplus_app','catalogo.reservar_codigo_producto(text,uuid)','EXECUTE')
                """, connection);
            Assert.Equal(true, await after.ExecuteScalarAsync());
        }
    }
}
