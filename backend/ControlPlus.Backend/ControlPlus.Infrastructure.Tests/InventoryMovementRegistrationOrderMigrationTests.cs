using System.Security.Cryptography;
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

public sealed class InventoryMovementRegistrationOrderMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261005010000_CustomerSearchIndexesV1";
    private const string Target = "20261014010000_InventoryMovementRegistrationOrderV1";
    private readonly Guid _user = Guid.CreateVersion7();
    private readonly Guid _firstProduct = Guid.CreateVersion7();
    private readonly Guid _secondProduct = Guid.CreateVersion7();
    private readonly LocalPostgresDatabase _postgres = new LocalPostgresDatabase();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_OrdersOnlyNewRowsUnderProductLockAndProtectsGeneratedValue()
    {
        await using var migrations = MigrationContext();
        var migrator = migrations.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedAsync();
        var historic = Guid.CreateVersion7();
        await InsertAsync(_firstProduct, historic);

        await migrator.MigrateAsync(Target);
        Assert.Null(await OrderAsync(historic));
        Assert.Equal(Target, (await migrations.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM pg_class WHERE oid='compras.seq_movimiento_inventario_orden_registro'::regclass AND relkind='S'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM pg_constraint WHERE conname='ck_movimiento_inventario_orden_registro_positivo' AND contype='c'"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM pg_indexes WHERE schemaname='compras' AND indexname='uq_movimiento_inventario_producto_orden_registro' AND indexdef LIKE '%WHERE (orden_registro IS NOT NULL)%'"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM pg_trigger WHERE tgrelid='compras.movimiento_inventario'::regclass AND tgname IN ('trg_movimiento_inventario_asignar_orden_registro','trg_movimiento_inventario_proteger_orden_registro') AND tgenabled='O'"));

        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var sameDate = Guid.CreateVersion7();
        var earlierUuid = Guid.Parse("00000000-0000-7000-8000-000000000002");
        var other = Guid.CreateVersion7();
        var firstDate = DateTime.UtcNow;
        await InsertAsync(_firstProduct, first, firstDate);
        await InsertAsync(_firstProduct, second, firstDate.AddDays(-2));
        await InsertAsync(_firstProduct, sameDate, firstDate);
        await InsertAsync(_firstProduct, earlierUuid, firstDate.AddDays(-3));
        await InsertAsync(_secondProduct, other, DateTime.UtcNow.AddDays(-3));
        Assert.True((await OrderAsync(first)) > 0);
        Assert.True((await OrderAsync(second)) > (await OrderAsync(first)));
        Assert.True((await OrderAsync(sameDate)) > (await OrderAsync(second)));
        Assert.True(earlierUuid.CompareTo(first) < 0);
        Assert.True((await OrderAsync(earlierUuid)) > (await OrderAsync(sameDate)));
        Assert.True((await OrderAsync(other)) > 0);
        Assert.NotEqual(await OrderAsync(first), await OrderAsync(other));

        await using (var firstConnection = await OpenAsync())
        await using (var secondConnection = await OpenAsync())
        {
            await using var firstTransaction = await firstConnection.BeginTransactionAsync();
            var held = Guid.CreateVersion7();
            await InsertAsync(firstConnection, firstTransaction, _firstProduct, held);
            var waiting = Guid.CreateVersion7();
            var pending = InsertAsync(secondConnection, null, _firstProduct, waiting);
            await Task.Delay(150);
            Assert.False(pending.IsCompleted);
            await firstTransaction.CommitAsync();
            await pending;
            Assert.True((await OrderAsync(waiting)) > (await OrderAsync(held)));
        }

        long rolledBackOrder;
        await using (var connection = await OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var rolledBack = Guid.CreateVersion7();
            await InsertAsync(connection, transaction, _firstProduct, rolledBack);
            rolledBackOrder = (await OrderAsync(connection, transaction, rolledBack))!.Value;
            await using (var update = new NpgsqlCommand(
                "UPDATE catalogo.producto SET stock_actual=5 WHERE id=@product", connection, transaction))
            {
                update.Parameters.AddWithValue("product", _firstProduct);
                await update.ExecuteNonQueryAsync();
            }
            await transaction.RollbackAsync();
            Assert.Null(await OrderAsync(rolledBack));
            Assert.Equal(0L, await ScalarAsync("SELECT stock_actual FROM catalogo.producto WHERE codigo_interno='ORDER-FIRST'"));
        }
        var afterRollback = Guid.CreateVersion7();
        await InsertAsync(_firstProduct, afterRollback);
        Assert.True((await OrderAsync(afterRollback)) > rolledBackOrder);

        await AssertSqlRejectedAsync("INSERT INTO compras.movimiento_inventario (id,producto_id,usuario_id,tipo_movimiento,cantidad_stock,stock_anterior,stock_posterior,reservado_anterior,reservado_posterior,correlacion_id,orden_registro) VALUES (@id,@product,@user,'VENTA',1,0,1,0,0,@id,999)");
        await AssertSqlRejectedAsync("UPDATE compras.movimiento_inventario SET orden_registro=999 WHERE id=@id", first);
        await AssertSqlRejectedAsync("UPDATE compras.movimiento_inventario SET producto_id=@product WHERE id=@id", first, _secondProduct);

        await using (var context = OfficialContext())
        {
            var movement = NewMovement(_firstProduct, Guid.CreateVersion7());
            context.MovimientoInventario.Add(movement);
            await context.SaveChangesAsync();
            Assert.True(movement.OrdenRegistro > (await OrderAsync(afterRollback)));
            Assert.Equal(movement.OrdenRegistro, context.Entry(movement).Property(x => x.OrdenRegistro).CurrentValue);
            Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Unchanged, context.Entry(movement).State);
            Assert.Equal(1L, await context.Producto.AsNoTracking().Where(x => x.Id == _firstProduct).Select(x => x.Version).SingleAsync());
        }

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM information_schema.columns WHERE table_schema='compras' AND table_name='movimiento_inventario' AND column_name='orden_registro'"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM pg_class WHERE relname='seq_movimiento_inventario_orden_registro' AND relnamespace='compras'::regnamespace"));
        Assert.True(await ScalarAsync("SELECT count(*) FROM compras.movimiento_inventario") > 0);
    }

    private async Task SeedAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO seguridad.usuario(id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
            VALUES (@user,'order-test','ORDER-TEST','Order test','unused-test-fixture','test','test');
            INSERT INTO catalogo.categoria(id,nombre,usuario_creacion_id)
            VALUES (gen_random_uuid(),'Order test',@user);
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,usuario_creacion_id)
            SELECT @first,(SELECT id FROM catalogo.categoria WHERE nombre='Order test'),id,'ORDER-FIRST','First',1,@user
            FROM catalogo.unidad_medida WHERE activo ORDER BY codigo LIMIT 1;
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,usuario_creacion_id)
            SELECT @second,(SELECT id FROM catalogo.categoria WHERE nombre='Order test'),id,'ORDER-SECOND','Second',1,@user
            FROM catalogo.unidad_medida WHERE activo ORDER BY codigo LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("user", _user);
        command.Parameters.AddWithValue("first", _firstProduct);
        command.Parameters.AddWithValue("second", _secondProduct);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertAsync(Guid product, Guid id, DateTime? date = null)
    {
        await using var connection = await OpenAsync();
        await InsertAsync(connection, null, product, id, date);
    }

    private async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid product, Guid id, DateTime? date = null)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO compras.movimiento_inventario
                (id,producto_id,usuario_id,tipo_movimiento,cantidad_stock,stock_anterior,stock_posterior,
                 reservado_anterior,reservado_posterior,correlacion_id,fecha_hora)
            VALUES (@id,@product,@user,'VENTA',1,0,1,0,0,@id,@date)
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("product", product);
        command.Parameters.AddWithValue("user", _user);
        command.Parameters.AddWithValue("date", date ?? DateTime.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long?> OrderAsync(Guid id)
    {
        await using var connection = await OpenAsync();
        return await OrderAsync(connection, null, id);
    }

    private static async Task<long?> OrderAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id)
    {
        await using var command = new NpgsqlCommand("SELECT orden_registro FROM compras.movimiento_inventario WHERE id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (long)result;
    }

    private async Task AssertSqlRejectedAsync(string sql, Guid? id = null, Guid? product = null)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id ?? Guid.CreateVersion7());
        command.Parameters.AddWithValue("product", product ?? _firstProduct);
        command.Parameters.AddWithValue("user", _user);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private MovimientoInventario NewMovement(Guid product, Guid id) => new()
    {
        Id = id, ProductoId = product, UsuarioId = _user, TipoMovimiento = "VENTA",
        CantidadStock = 1, StockAnterior = 0, StockPosterior = 1,
        ReservadoAnterior = 0, ReservadoPosterior = 0, CorrelacionId = id,
        FechaHora = DateTime.UtcNow
    };

    private ControlPlusDbContext MigrationContext() => new(new DbContextOptionsBuilder<ControlPlusDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).Options);

    private OfficialControlPlusDbContext OfficialContext() => new(new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).Options);
}
