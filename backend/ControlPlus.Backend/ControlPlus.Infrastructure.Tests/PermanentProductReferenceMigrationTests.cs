using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class PermanentProductReferenceMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261028010000_SaleReceiptDataSnapshotV1";
    private const string Target = "20261029010000_PermanentProductReferencesV1";
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly Guid _first = Guid.CreateVersion7();
    private readonly Guid _second = Guid.CreateVersion7();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory, InlineData("CODE128"), InlineData("EAN13"), Trait("Category", "Integration")]
    public async Task Upgrade_PreservesHistoryBarcodesAndSnapshots_ReservesAliasesAndGrowsBeyondSixDigits(string legacyFormat)
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalogo.producto SET formato_codigo_barras={legacyFormat} WHERE id={_first}");
        var before = await SnapshotAsync();
        await migrator.MigrateAsync(Target);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(Target, (await db.Database.GetAppliedMigrationsAsync()).Last());
        await using var official = new OfficialControlPlusDbContext(new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var first = await official.Producto.AsNoTracking().SingleAsync(x => x.Id == _first);
        var second = await official.Producto.AsNoTracking().SingleAsync(x => x.Id == _second);
        Assert.Matches("^P-[0-9]{6,}$", first.CodigoInterno);
        Assert.NotEqual(first.CodigoInterno, second.CodigoInterno);
        Assert.Equal("CP-LEGACY", first.CodigoBarras);
        Assert.True(first.CodigoBarrasGenerado);
        Assert.Equal(legacyFormat, first.FormatoCodigoBarras);
        Assert.Equal("0000123456789", second.CodigoBarras);
        Assert.Equal("EAN13", second.FormatoCodigoBarras);
        Assert.False(second.Activo);
        Assert.Equal(2L, first.Version);
        Assert.Equal(_first, (await official.ProductoCodigo.SingleAsync(x => x.Codigo == "PRD-OLD-1")).ProductoId);
        Assert.Equal(_second, (await official.ProductoCodigo.SingleAsync(x => x.Codigo == "PRD-OLD-2")).ProductoId);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var collision = new NpgsqlCommand(
            "UPDATE catalogo.producto SET codigo_barras=@code, formato_codigo_barras=NULL WHERE id=@id", connection))
        {
            collision.Parameters.AddWithValue("code", first.CodigoInterno);
            collision.Parameters.AddWithValue("id", _second);
            var error = await Assert.ThrowsAsync<PostgresException>(() => collision.ExecuteNonQueryAsync());
            Assert.Equal("23505", error.SqlState);
        }
        await using (var immutable = new NpgsqlCommand("UPDATE catalogo.producto SET codigo_interno='P-999999' WHERE id=@id", connection))
        {
            immutable.Parameters.AddWithValue("id", _first);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => immutable.ExecuteNonQueryAsync())).SqlState);
        }
        await using (var sequence = new NpgsqlCommand("SELECT setval('catalogo.producto_referencia_seq', 999999)", connection))
            await sequence.ExecuteNonQueryAsync();
        await using (var next = new NpgsqlCommand("SELECT catalogo.siguiente_referencia_producto()", connection))
            Assert.Equal("P-1000000", await next.ExecuteScalarAsync());
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(Target, (await db.Database.GetAppliedMigrationsAsync()).Last());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Upgrade_SkipsReferenceAlreadyUsedAsExternalBarcode()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalogo.producto SET codigo_barras='P-000001' WHERE id={_second}");
        await migrator.MigrateAsync(Target);
        await using var official = new OfficialControlPlusDbContext(new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var products = await official.Producto.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, products.Length);
        Assert.All(products, product => Assert.NotEqual("P-000001", product.CodigoInterno));
        Assert.Equal("P-000001", products.Single(product => product.Id == _second).CodigoBarras);
        Assert.Equal(_second, (await official.ProductoCodigo.SingleAsync(code => code.Codigo == "P-000001")).ProductoId);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Upgrade_RejectsAmbiguousLegacyCodesWithoutPartialChanges()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("UPDATE catalogo.producto SET codigo_barras='PRD-OLD-1' WHERE id=@id", connection))
        {
            command.Parameters.AddWithValue("id", _second);
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Target))).SqlState);
        Assert.DoesNotContain(Target, await db.Database.GetAppliedMigrationsAsync());
        await using var query = new NpgsqlCommand("SELECT to_regclass('catalogo.producto_codigo') IS NULL", connection);
        Assert.Equal(true, await query.ExecuteScalarAsync());
    }

    private async Task SeedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO seguridad.usuario(id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
            VALUES (@user,'reference-fixture','REFERENCE-FIXTURE','Migration fixture','unused','test','test');
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion) VALUES(@est,'Reference fixture','REFERENCE');
            INSERT INTO configuracion.instalacion(id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
            VALUES(@install,@est,'REFERENCE','TEST','test','test');
            INSERT INTO configuracion.terminal(id,instalacion_id,codigo,nombre) VALUES(@terminal,@install,'TEST','Fixture');
            INSERT INTO catalogo.categoria(id,nombre,usuario_creacion_id) VALUES(@category,'Fixture',@user);
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,codigo_barras,formato_codigo_barras,codigo_barras_generado,nombre,precio_minorista,usuario_creacion_id)
            SELECT @first,@category,id,'PRD-OLD-1','CP-LEGACY','CODE128',true,'First',100,@user FROM catalogo.unidad_medida WHERE codigo='UNIDAD';
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,codigo_barras,formato_codigo_barras,nombre,precio_minorista,usuario_creacion_id,activo)
            SELECT @second,@category,id,'PRD-OLD-2','0000123456789','EAN13','Second',100,@user,false FROM catalogo.unidad_medida WHERE codigo='UNIDAD';
            INSERT INTO compras.movimiento_inventario(id,producto_id,usuario_id,tipo_movimiento,cantidad_stock,stock_anterior,stock_posterior,reservado_anterior,reservado_posterior,correlacion_id)
            VALUES(gen_random_uuid(),@first,@user,'COMPRA',1,0,1,0,0,gen_random_uuid());
            INSERT INTO auditoria.operacion_idempotente(id,instalacion_id,terminal_id,usuario_id,clave_idempotencia,tipo_operacion,hash_solicitud,entidad,entidad_id,resultado,correlacion_id)
            VALUES(gen_random_uuid(),@install,@terminal,@user,'legacy-key','CREAR_PRODUCTO','original-hash','Producto',@first,
                jsonb_build_object('InternalCode','PRD-OLD-1','Barcode','CP-LEGACY'),gen_random_uuid());
            INSERT INTO auditoria.evento_auditoria(id,usuario_id,instalacion_id,accion,entidad,entidad_id,resultado,datos_nuevos,correlacion_id)
            VALUES(gen_random_uuid(),@user,@install,'ProductCreated','Producto',@first,'EXITOSO',jsonb_build_object('CodigoInterno','PRD-OLD-1'),gen_random_uuid());
            """, connection);
        foreach (var name in new[] { "user", "est", "install", "terminal", "category" })
            command.Parameters.AddWithValue(name, Guid.CreateVersion7());
        command.Parameters.AddWithValue("first", _first);
        command.Parameters.AddWithValue("second", _second);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> SnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'movements',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM compras.movimiento_inventario m),
              'idempotency',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM auditoria.operacion_idempotente i),
              'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM auditoria.evento_auditoria a))::text
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
