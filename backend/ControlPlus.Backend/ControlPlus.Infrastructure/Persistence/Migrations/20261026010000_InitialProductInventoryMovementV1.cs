using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261026010000_InitialProductInventoryMovementV1")]
public sealed class InitialProductInventoryMovementV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE compras.movimiento_inventario
                DROP CONSTRAINT ck_movimiento_inventario_tipo;
            ALTER TABLE compras.movimiento_inventario
                ADD CONSTRAINT ck_movimiento_inventario_tipo CHECK (
                    tipo_movimiento IN (
                        'COMPRA', 'ANULACION_COMPRA', 'VENTA', 'ANULACION_VENTA',
                        'RESERVA_APARTADO', 'LIBERACION_APARTADO', 'ENTREGA_APARTADO',
                        'CAMBIO_DEVOLUCION', 'CAMBIO_ENTREGA', 'AJUSTE_ENTRADA',
                        'AJUSTE_SALIDA', 'REVERSO', 'INVENTARIO_INICIAL'
                    )
                );
            ALTER TABLE compras.movimiento_inventario
                ADD CONSTRAINT ck_movimiento_inventario_inicial CHECK (
                    tipo_movimiento <> 'INVENTARIO_INICIAL' OR (
                        cantidad_stock > 0 AND cantidad_reservada = 0
                        AND stock_anterior = 0 AND stock_posterior = cantidad_stock
                        AND reservado_anterior = 0 AND reservado_posterior = 0
                        AND costo_promedio_anterior IS NULL AND costo_promedio_posterior IS NOT NULL
                        AND sesion_operador_id IS NULL AND autorizacion_operacion_id IS NULL
                        AND motivo_operacion_id IS NULL AND venta_id IS NULL AND compra_id IS NULL
                        AND apartado_id IS NULL AND cambio_venta_id IS NULL AND anulacion_venta_id IS NULL
                        AND movimiento_revertido_id IS NULL
                    )
                );
            CREATE UNIQUE INDEX uq_movimiento_inventario_inicial_producto
                ON compras.movimiento_inventario (producto_id)
                WHERE tipo_movimiento = 'INVENTARIO_INICIAL';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $block$
            BEGIN
                IF EXISTS (SELECT 1 FROM compras.movimiento_inventario
                           WHERE tipo_movimiento = 'INVENTARIO_INICIAL') THEN
                    RAISE EXCEPTION 'Initial inventory movements must be preserved';
                END IF;
            END;
            $block$;
            DROP INDEX compras.uq_movimiento_inventario_inicial_producto;
            ALTER TABLE compras.movimiento_inventario
                DROP CONSTRAINT ck_movimiento_inventario_inicial;
            ALTER TABLE compras.movimiento_inventario
                DROP CONSTRAINT ck_movimiento_inventario_tipo;
            ALTER TABLE compras.movimiento_inventario
                ADD CONSTRAINT ck_movimiento_inventario_tipo CHECK (
                    tipo_movimiento IN (
                        'COMPRA', 'ANULACION_COMPRA', 'VENTA', 'ANULACION_VENTA',
                        'RESERVA_APARTADO', 'LIBERACION_APARTADO', 'ENTREGA_APARTADO',
                        'CAMBIO_DEVOLUCION', 'CAMBIO_ENTREGA', 'AJUSTE_ENTRADA',
                        'AJUSTE_SALIDA', 'REVERSO'
                    )
                );
            """);
    }
}
