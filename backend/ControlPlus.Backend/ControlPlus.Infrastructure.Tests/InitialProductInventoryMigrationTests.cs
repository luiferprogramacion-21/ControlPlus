using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class InitialProductInventoryMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261021010000_LayawayCancellationOptionalReasonV1";
    private const string Target = "20261026010000_InitialProductInventoryMovementV1";
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly Guid _user = Guid.CreateVersion7();
    private readonly Guid _first = Guid.CreateVersion7();
    private readonly Guid _second = Guid.CreateVersion7();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_PreservesHistoryAndEnforcesInitialMovementShapeAndUniqueness()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO seguridad.usuario(id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
            VALUES (@user,'initial-migration','INITIAL-MIGRATION','Prueba','test','test','test');
            INSERT INTO catalogo.categoria(id,nombre,usuario_creacion_id) VALUES (gen_random_uuid(),'Inicial',@user);
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,usuario_creacion_id)
            SELECT @first,c.id,u.id,'INITIAL-M1','Uno',100,@user
            FROM catalogo.categoria c CROSS JOIN catalogo.unidad_medida u WHERE c.nombre='Inicial' AND u.codigo='UNIDAD';
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,usuario_creacion_id)
            SELECT @second,c.id,u.id,'INITIAL-M2','Dos',100,@user
            FROM catalogo.categoria c CROSS JOIN catalogo.unidad_medida u WHERE c.nombre='Inicial' AND u.codigo='UNIDAD';
            INSERT INTO compras.movimiento_inventario(id,producto_id,usuario_id,tipo_movimiento,cantidad_stock,
              stock_anterior,stock_posterior,reservado_anterior,reservado_posterior,correlacion_id)
            VALUES (gen_random_uuid(),@first,@user,'VENTA',1,0,1,0,0,gen_random_uuid());
            """, connection))
        {
            seed.Parameters.AddWithValue("user", _user);
            seed.Parameters.AddWithValue("first", _first);
            seed.Parameters.AddWithValue("second", _second);
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(Target);
        await migrator.MigrateAsync(Previous);
        await migrator.MigrateAsync(Target);
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM compras.movimiento_inventario WHERE tipo_movimiento='VENTA'"));

        await AssertRejectedAsync(connection, _second, "-1, 1, 0, 0, 0, 10, NULL", "23514");
        await AssertRejectedAsync(connection, _second, "1, 0, 1, 0, 0, NULL, NULL", "23514");
        await AssertRejectedAsync(connection, _second, "1, 0, 1, 0, 0, 10, gen_random_uuid()", "23514");
        await InsertInitialAsync(connection, _second, "1, 0, 1, 0, 0, 0, NULL");
        await AssertRejectedAsync(connection, _second, "1, 0, 1, 0, 0, 10, NULL", "23505");

        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(Target, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(1L, await CountAsync(connection, "SELECT count(*) FROM compras.movimiento_inventario WHERE tipo_movimiento='INVENTARIO_INICIAL'"));
    }

    private async Task AssertRejectedAsync(NpgsqlConnection connection, Guid product, string values, string state)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertInitialAsync(connection, product, values));
        Assert.Equal(state, error.SqlState);
    }

    private async Task InsertInitialAsync(NpgsqlConnection connection, Guid product, string values)
    {
        // values are test-owned fixed SQL expressions, not user input.
        await using var command = new NpgsqlCommand($"""
            INSERT INTO compras.movimiento_inventario
                (id,producto_id,usuario_id,tipo_movimiento,cantidad_stock,stock_anterior,stock_posterior,
                 reservado_anterior,reservado_posterior,costo_promedio_posterior,compra_id,correlacion_id)
            VALUES (gen_random_uuid(),@product,@user,'INVENTARIO_INICIAL',{values},gen_random_uuid())
            """, connection);
        command.Parameters.AddWithValue("product", product);
        command.Parameters.AddWithValue("user", _user);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
