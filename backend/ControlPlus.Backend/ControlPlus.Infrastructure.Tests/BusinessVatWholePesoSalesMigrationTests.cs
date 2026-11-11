using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class BusinessVatWholePesoSalesMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261110010000_BusinessVatSettingV1";
    private const string Target = "20261110020000_BusinessVatWholePesoSalesV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task WholePesoCheck_RejectsBeforeMigration_AcceptsAfter_AndPreservesFourDecimalRows()
    {
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(await HasBothRoundingsAsync());

        var user = Guid.CreateVersion7();
        var establishment = Guid.CreateVersion7();
        var installation = Guid.CreateVersion7();
        var terminal = Guid.CreateVersion7();
        var register = Guid.CreateVersion7();
        var shift = Guid.CreateVersion7();
        var category = Guid.CreateVersion7();
        var product = Guid.CreateVersion7();
        var sale = Guid.CreateVersion7();
        var historicalDetail = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var seedTransaction = await connection.BeginTransactionAsync();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO seguridad.usuario(id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
            VALUES (@user,'vat-migration','VAT-MIGRATION','IVA prueba','unused','test','test');
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES (@establishment,'IVA prueba','TEST-VAT');
            INSERT INTO configuracion.instalacion(id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
            VALUES (@installation,@establishment,'TEST-VAT','TEST','test','test');
            INSERT INTO configuracion.terminal(id,instalacion_id,codigo,nombre)
            VALUES (@terminal,@installation,'TEST-VAT','IVA prueba');
            INSERT INTO caja.caja(id,instalacion_id,codigo,nombre)
            VALUES (@register,@installation,'TEST-VAT','IVA prueba');
            INSERT INTO caja.turno_caja(id,caja_id,terminal_id,modo_operacion,usuario_apertura_id,usuario_responsable_id,fecha_operativa,monto_inicial)
            VALUES (@shift,@register,@terminal,'INDIVIDUAL',@user,@user,CURRENT_DATE,0);
            INSERT INTO catalogo.categoria(id,nombre,abreviatura,usuario_creacion_id)
            VALUES (@category,'IVA prueba','IVA',@user);
            INSERT INTO catalogo.producto(id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,usuario_creacion_id)
            SELECT @product,@category,id,'IVA-TEST-000001','IVA prueba',1000,@user
            FROM catalogo.unidad_medida WHERE activo ORDER BY codigo LIMIT 1;
            INSERT INTO ventas.venta(id,instalacion_id,turno_caja_id,usuario_id,prefijo,serie,consecutivo,
              fecha_hora,fecha_operativa,fecha_limite_cambio,tipo_venta,subtotal_bruto,
              base_gravable_total,impuesto_incluido_total,iva_discriminado_completo,total)
            VALUES (@sale,@installation,@shift,@user,'TEST','VAT',1,clock_timestamp(),CURRENT_DATE,CURRENT_DATE,
              'CONTADO',1000,840.3361,159.6639,true,1000);
            INSERT INTO ventas.detalle_venta(id,venta_id,producto_id,codigo_producto,nombre_producto,
              unidad_medida,cantidad,tipo_precio,precio_unitario,subtotal_bruto,subtotal_neto,
              porcentaje_iva,base_gravable,valor_iva)
            VALUES (@historicalDetail,@sale,@product,'IVA-TEST-000001','IVA prueba','UNIDAD',1,'MINORISTA',
              1000,1000,1000,19,840.3361,159.6639);
            INSERT INTO ventas.pago_venta(id,venta_id,metodo_pago_id,usuario_id,valor_aplicado,valor_recibido,cambio)
            SELECT gen_random_uuid(),@sale,id,@user,1000,1000,0
            FROM catalogo.metodo_pago WHERE codigo='EFECTIVO';
            """, connection, seedTransaction))
        {
            seed.Parameters.AddWithValue("user", user);
            seed.Parameters.AddWithValue("establishment", establishment);
            seed.Parameters.AddWithValue("installation", installation);
            seed.Parameters.AddWithValue("terminal", terminal);
            seed.Parameters.AddWithValue("register", register);
            seed.Parameters.AddWithValue("shift", shift);
            seed.Parameters.AddWithValue("category", category);
            seed.Parameters.AddWithValue("product", product);
            seed.Parameters.AddWithValue("sale", sale);
            seed.Parameters.AddWithValue("historicalDetail", historicalDetail);
            await seed.ExecuteNonQueryAsync();
        }
        await seedTransaction.CommitAsync();
        Assert.Equal(1L, await CountDetailsAsync(connection));

        async Task InsertDetailAsync(NpgsqlConnection targetConnection, NpgsqlTransaction? transaction, Guid targetSale)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO ventas.detalle_venta(id,venta_id,producto_id,codigo_producto,nombre_producto,
                  unidad_medida,cantidad,tipo_precio,precio_unitario,subtotal_bruto,subtotal_neto,
                  porcentaje_iva,base_gravable,valor_iva)
                VALUES (@id,@sale,@product,'IVA-TEST-000001','IVA prueba','UNIDAD',1,'MINORISTA',
                  1000,1000,1000,19,840,160)
                """, targetConnection, transaction);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("sale", targetSale);
            command.Parameters.AddWithValue("product", product);
            await command.ExecuteNonQueryAsync();
        }

        await using (var rejectedConnection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await rejectedConnection.OpenAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => InsertDetailAsync(rejectedConnection, null, sale));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
        }
        Assert.Equal(1L, await CountDetailsAsync(connection));

        await migrator.MigrateAsync(Target);
        Assert.Equal(Target, (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.True(await HasBothRoundingsAsync());
        Assert.Equal(1L, await CountDetailsAsync(connection));
        // A confirmed sale is not edited: a second complete sale carries the whole-peso breakdown.
        var secondSale = Guid.CreateVersion7();
        await using (var secondSaleTransaction = await connection.BeginTransactionAsync())
        {
            await using var header = new NpgsqlCommand("""
                INSERT INTO ventas.venta(id,instalacion_id,turno_caja_id,usuario_id,prefijo,serie,consecutivo,
                  fecha_hora,fecha_operativa,fecha_limite_cambio,tipo_venta,subtotal_bruto,
                  base_gravable_total,impuesto_incluido_total,iva_discriminado_completo,total)
                VALUES (@secondSale,@installation,@shift,@user,'TEST','VAT',2,clock_timestamp(),CURRENT_DATE,CURRENT_DATE,
                  'CONTADO',1000,840,160,true,1000);
                """, connection, secondSaleTransaction);
            header.Parameters.AddWithValue("secondSale", secondSale);
            header.Parameters.AddWithValue("installation", installation);
            header.Parameters.AddWithValue("shift", shift);
            header.Parameters.AddWithValue("user", user);
            await header.ExecuteNonQueryAsync();
            await InsertDetailAsync(connection, secondSaleTransaction, secondSale);
            await using var payment = new NpgsqlCommand("""
                INSERT INTO ventas.pago_venta(id,venta_id,metodo_pago_id,usuario_id,valor_aplicado,valor_recibido,cambio)
                SELECT gen_random_uuid(),@secondSale,id,@user,1000,1000,0
                FROM catalogo.metodo_pago WHERE codigo='EFECTIVO';
                """, connection, secondSaleTransaction);
            payment.Parameters.AddWithValue("secondSale", secondSale);
            payment.Parameters.AddWithValue("user", user);
            await payment.ExecuteNonQueryAsync();
            await secondSaleTransaction.CommitAsync();
        }
        Assert.Equal(2L, await CountDetailsAsync(connection));
    }

    private async Task<bool> HasBothRoundingsAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_constraint
              WHERE conrelid='ventas.detalle_venta'::regclass
                AND conname='ck_detalle_venta_iva_incluido'
                AND position(', 4)' in pg_get_constraintdef(oid)) > 0
                AND position(', 0)' in pg_get_constraintdef(oid)) > 0)
            """, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountDetailsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM ventas.detalle_venta", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
