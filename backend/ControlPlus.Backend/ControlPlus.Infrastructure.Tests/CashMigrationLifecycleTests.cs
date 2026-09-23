using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class CashMigrationLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UpDown_PreservesInitialAndPreexistingPaymentMethods(bool preexisting, bool customNames)
    {
        await using var database = await CashMigrationDatabase.StartAsync(applyCash: false);
        Assert.Equal(4L, await database.ScalarAsync<long>("SELECT count(*) FROM \"__EFMigrationsHistory\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM catalogo.metodo_pago"));
        if (preexisting)
        {
            await database.ExecuteAsync($"""
                INSERT INTO catalogo.metodo_pago(id,codigo,nombre,afecta_efectivo,requiere_referencia,
                    orden_visual,activo,version,fecha_creacion)
                VALUES (gen_random_uuid(),'EFECTIVO','{(customNames ? "Billetes locales" : "Efectivo")}',true,false,3,true,9,'2026-01-01Z'),
                    (gen_random_uuid(),'TRANSFERENCIA','{(customNames ? "Cuenta bancaria local" : "Transferencia")}',false,true,7,false,8,'2026-01-02Z'),
                    (gen_random_uuid(),'NEQUI','{(customNames ? "Billetera local" : "Nequi")}',false,true,99,true,7,'2026-01-03Z');
                """);
        }
        var original = await database.RowsAsync("catalogo.metodo_pago");
        await database.MigrateAsync(CashMigrationDatabase.CashMigration);
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM catalogo.metodo_pago"));
        var afterUp = await database.RowsAsync("catalogo.metodo_pago");
        if (preexisting) Assert.Equal(original, afterUp);
        await database.MigrateAsync(CashMigrationDatabase.PreviousMigration);
        Assert.Equal(afterUp, await database.RowsAsync("catalogo.metodo_pago"));
        Assert.Equal(4L, await database.ScalarAsync<long>("SELECT count(*) FROM \"__EFMigrationsHistory\""));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM information_schema.tables WHERE table_schema='caja' AND table_name IN ('detalle_arqueo_medio_pago','autorizacion_cierre_turno')"));
        // Reapplication with retained catalog must also be idempotent.
        await database.MigrateAsync(CashMigrationDatabase.CashMigration);
        Assert.Equal(afterUp, await database.RowsAsync("catalogo.metodo_pago"));
    }

    [Fact]
    public async Task Down_PreservesPaymentMethodsWithExistingForeignKeyReferences()
    {
        await using var database = await CashMigrationDatabase.StartAsync();
        await database.ExecuteAsync($"""
            CREATE TABLE public.referencia_metodo_pago_prueba (
                id uuid PRIMARY KEY,
                metodo_pago_id uuid NOT NULL REFERENCES catalogo.metodo_pago(id) ON DELETE RESTRICT);
            INSERT INTO public.referencia_metodo_pago_prueba(id,metodo_pago_id)
            SELECT gen_random_uuid(),id FROM catalogo.metodo_pago;
            """);
        var methods = await database.RowsAsync("catalogo.metodo_pago");
        var references = await database.RowsAsync("public.referencia_metodo_pago_prueba");
        await database.MigrateAsync(CashMigrationDatabase.PreviousMigration);
        Assert.Equal(methods, await database.RowsAsync("catalogo.metodo_pago"));
        Assert.Equal(references, await database.RowsAsync("public.referencia_metodo_pago_prueba"));
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM public.referencia_metodo_pago_prueba r JOIN catalogo.metodo_pago m ON m.id=r.metodo_pago_id"));
    }

    [Theory]
    [InlineData("ABIERTA", 0)]
    [InlineData("CERRADA", 0)]
    [InlineData("CERRADA", 20)]
    public async Task Up_RefusesEveryHistoricalShiftBeforeChangingSchemaOrData(string state, int difference)
    {
        await using var database = await CashMigrationDatabase.StartAsync(applyCash: false);
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        if (state == "CERRADA")
            await database.ExecuteAsync($"""
                UPDATE caja.turno_caja SET estado='CERRADA',usuario_cierre_id='{CashMigrationDatabase.Executor}',
                    fecha_hora_cierre=clock_timestamp(),efectivo_esperado=100,efectivo_contado={100 + difference},
                    diferencia={difference},motivo_diferencia_id={(difference == 0 ? "NULL" : $"'{CashMigrationDatabase.Reason}'::uuid")}
                WHERE id='{shift}';
                """);
        var schema = await database.SchemaAsync();
        var shifts = await database.RowsAsync("caja.turno_caja");
        var catalog = await database.RowsAsync("catalogo.metodo_pago");
        var history = await database.RowsAsync("\"__EFMigrationsHistory\"");
        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.MigrateAsync(CashMigrationDatabase.CashMigration));
        Assert.Contains("transici", exception.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(schema, await database.SchemaAsync());
        Assert.Equal(shifts, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(catalog, await database.RowsAsync("catalogo.metodo_pago"));
        Assert.Equal(history, await database.RowsAsync("\"__EFMigrationsHistory\""));
    }

    [Fact]
    public async Task Down_WithClosedShift_IsRejectedAndRollsBackSchemaDataAndHistory()
    {
        await using var database = await CashMigrationDatabase.StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        await database.ExecuteTransactionAsync(
            CashMigrationDatabase.DetailSql(shift),
            CashMigrationDatabase.CloseSql(shift));

        var schema = await database.SchemaAsync();
        var columns = await database.ColumnsAsync();
        var constraints = await database.ConstraintsAsync();
        var indexes = await database.IndexesAsync();
        var history = await database.HistoryAsync();
        var shifts = await database.RowsAsync("caja.turno_caja");
        var details = await database.RowsAsync("caja.detalle_arqueo_medio_pago");
        var methods = await database.RowsAsync("catalogo.metodo_pago");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            database.MigrateAsync(CashMigrationDatabase.PreviousMigration));
        Assert.Contains("cierres historicos", exception.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(schema, await database.SchemaAsync());
        Assert.Equal(columns, await database.ColumnsAsync());
        Assert.Equal(constraints, await database.ConstraintsAsync());
        Assert.Equal(indexes, await database.IndexesAsync());
        Assert.Equal(history, await database.HistoryAsync());
        Assert.Equal(shifts, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(details, await database.RowsAsync("caja.detalle_arqueo_medio_pago"));
        Assert.Equal(methods, await database.RowsAsync("catalogo.metodo_pago"));
        Assert.Equal(1L, await database.ScalarAsync<long>($"""
            SELECT count(*) FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '{CashMigrationDatabase.CashMigration}'
            """));
        Assert.Equal(3L, await database.ScalarAsync<long>($"""
            SELECT count(*) FROM caja.detalle_arqueo_medio_pago WHERE turno_caja_id = '{shift}'
            """));
    }

    [Fact]
    public async Task Down_WithCombinedPaymentsSharingMovement_IsRejectedAndRollsBackEverything()
    {
        await using var database = await CashMigrationDatabase.StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        var movement = await SeedCombinedPaymentsAsync(database, shift);

        var schema = await database.SchemaAsync();
        var columns = await database.ColumnsAsync();
        var constraints = await database.ConstraintsAsync();
        var indexes = await database.IndexesAsync();
        var history = await database.HistoryAsync();
        var shifts = await database.RowsAsync("caja.turno_caja");
        var movements = await database.RowsAsync("caja.movimiento_caja");
        var sales = await database.RowsAsync("ventas.venta");
        var payments = await database.RowsAsync("ventas.pago_venta");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            database.MigrateAsync(CashMigrationDatabase.PreviousMigration));
        Assert.Contains("pagos combinados", exception.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(schema, await database.SchemaAsync());
        Assert.Equal(columns, await database.ColumnsAsync());
        Assert.Equal(constraints, await database.ConstraintsAsync());
        Assert.Equal(indexes, await database.IndexesAsync());
        Assert.Equal(history, await database.HistoryAsync());
        Assert.Equal(shifts, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(movements, await database.RowsAsync("caja.movimiento_caja"));
        Assert.Equal(sales, await database.RowsAsync("ventas.venta"));
        Assert.Equal(payments, await database.RowsAsync("ventas.pago_venta"));
        Assert.Equal(2L, await database.ScalarAsync<long>($"""
            SELECT count(*) FROM ventas.pago_venta WHERE movimiento_caja_id = '{movement}'
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>($"""
            SELECT count(*) FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '{CashMigrationDatabase.CashMigration}'
            """));
    }

    private static async Task<Guid> SeedCombinedPaymentsAsync(CashMigrationDatabase database, Guid shift)
    {
        var movement = Guid.CreateVersion7();
        var sale = Guid.CreateVersion7();
        var category = Guid.CreateVersion7();
        var product = Guid.CreateVersion7();
        var detail = Guid.CreateVersion7();
        await database.ExecuteAsync($"""
            INSERT INTO caja.movimiento_caja
              (id,turno_caja_id,usuario_id,tipo,categoria_movimiento,valor,fecha_hora,correlacion_id)
            VALUES ('{movement}','{shift}','{CashMigrationDatabase.Executor}',
              'INGRESO','PAGO_VENTA',30,clock_timestamp(),gen_random_uuid());
            INSERT INTO catalogo.categoria(id,nombre,usuario_creacion_id)
            VALUES ('{category}','Categoria combinada aislada','{CashMigrationDatabase.Executor}');
            INSERT INTO catalogo.producto
              (id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,usuario_creacion_id)
            SELECT '{product}','{category}',id,'COMBINED-{product:N}','Producto combinado aislado',
              30,'{CashMigrationDatabase.Executor}'
            FROM catalogo.unidad_medida WHERE activo ORDER BY codigo LIMIT 1;
            INSERT INTO ventas.venta
              (id,instalacion_id,turno_caja_id,usuario_id,prefijo,serie,consecutivo,
               fecha_hora,fecha_operativa,fecha_limite_cambio,tipo_venta,subtotal_bruto,total)
            VALUES ('{sale}','{CashMigrationDatabase.Installation}','{shift}',
              '{CashMigrationDatabase.Executor}','TEST','COMBINED',1,clock_timestamp(),
              CURRENT_DATE,CURRENT_DATE,'CONTADO',30,30);
            INSERT INTO ventas.detalle_venta
              (id,venta_id,producto_id,codigo_producto,nombre_producto,unidad_medida,
               cantidad,tipo_precio,precio_unitario,subtotal_bruto,subtotal_neto)
            VALUES ('{detail}','{sale}','{product}','COMBINED-{product:N}',
              'Producto combinado aislado','UNIDAD',1,'MINORISTA',30,30,30);
            INSERT INTO ventas.pago_venta
              (id,venta_id,metodo_pago_id,movimiento_caja_id,usuario_id,
               valor_aplicado,valor_recibido,cambio,referencia,fecha_hora)
            SELECT gen_random_uuid(),'{sale}',id,'{movement}','{CashMigrationDatabase.Executor}',
              CASE codigo WHEN 'TRANSFERENCIA' THEN 10 ELSE 20 END,NULL,0,
              'COMBINED-' || codigo,clock_timestamp()
            FROM catalogo.metodo_pago WHERE codigo IN ('TRANSFERENCIA','NEQUI');
            """);
        return movement;
    }
}
