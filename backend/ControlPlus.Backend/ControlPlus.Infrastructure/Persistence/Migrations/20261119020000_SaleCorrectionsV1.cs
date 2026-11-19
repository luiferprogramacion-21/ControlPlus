using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>Allows a correction that returns original sale items without a replacement. Keeps all settlement guards.</summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261119020000_SaleCorrectionsV1")]
public sealed class SaleCorrectionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION ventas.validar_liquidacion_cambio()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        DECLARE
            v_cambio_id uuid;
            v_cambio ventas.cambio_venta%ROWTYPE;
            v_cobros numeric;
            v_devoluciones numeric;
            v_cantidad_ajustes integer;
            v_tipo_ajuste varchar(20);
            v_valor_ajuste numeric;
            v_credito_ajuste uuid;
            v_total_devuelto numeric;
            v_total_entregado numeric;
            v_lineas_devueltas integer;
            v_lineas_entregadas integer;
        BEGIN
            IF TG_TABLE_NAME = 'cambio_venta' THEN
                v_cambio_id := NEW.id;
            ELSIF TG_OP = 'DELETE' THEN
                v_cambio_id := OLD.cambio_venta_id;
            ELSE
                v_cambio_id := NEW.cambio_venta_id;
            END IF;
        
            SELECT *
              INTO v_cambio
              FROM ventas.cambio_venta
             WHERE id = v_cambio_id;
        
            IF NOT FOUND THEN
                IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                END IF;
                RETURN NEW;
            END IF;
        
            IF v_cambio.estado = 'CONFIRMADO' THEN
                SELECT COALESCE(sum(subtotal_neto) FILTER (
                           WHERE tipo_detalle = 'DEVUELTO'
                       ), 0),
                       COALESCE(sum(subtotal_neto) FILTER (
                           WHERE tipo_detalle = 'ENTREGADO'
                       ), 0),
                       count(*) FILTER (WHERE tipo_detalle = 'DEVUELTO'),
                       count(*) FILTER (WHERE tipo_detalle = 'ENTREGADO')
                  INTO v_total_devuelto,
                       v_total_entregado,
                       v_lineas_devueltas,
                       v_lineas_entregadas
                  FROM ventas.detalle_cambio_venta
                 WHERE cambio_venta_id = v_cambio_id;
        
                IF v_lineas_devueltas = 0
                   OR v_total_devuelto <> v_cambio.total_devuelto
                   OR v_total_entregado <> v_cambio.total_entregado
                THEN
                    RAISE EXCEPTION
                        'Los totales del cambio no coinciden con los productos devueltos y entregados.';
                END IF;
        
                SELECT COALESCE(sum(valor) FILTER (
                           WHERE estado = 'APLICADO' AND tipo_movimiento = 'COBRO'
                       ), 0),
                       COALESCE(sum(valor) FILTER (
                           WHERE estado = 'APLICADO' AND tipo_movimiento = 'DEVOLUCION'
                       ), 0)
                  INTO v_cobros, v_devoluciones
                  FROM ventas.pago_cambio_venta
                 WHERE cambio_venta_id = v_cambio_id;
        
                IF (v_cambio.diferencia > 0
                    AND (v_cobros <> v_cambio.valor_pago_o_devolucion
                         OR v_devoluciones <> 0))
                   OR (v_cambio.diferencia < 0
                       AND (v_devoluciones <> v_cambio.valor_pago_o_devolucion
                            OR v_cobros <> 0))
                   OR (v_cambio.diferencia = 0
                       AND (v_cobros <> 0 OR v_devoluciones <> 0))
                THEN
                    RAISE EXCEPTION
                        'Los cobros o devoluciones no coinciden con la diferencia del cambio.';
                END IF;
        
                SELECT count(*)
                  INTO v_cantidad_ajustes
                  FROM ventas.ajuste_credito_cambio
                 WHERE cambio_venta_id = v_cambio_id;
        
                IF v_cantidad_ajustes = 1 THEN
                    SELECT tipo_ajuste, valor, credito_id
                      INTO v_tipo_ajuste, v_valor_ajuste, v_credito_ajuste
                      FROM ventas.ajuste_credito_cambio
                     WHERE cambio_venta_id = v_cambio_id;
                END IF;
        
                IF v_cambio.valor_ajuste_credito = 0 AND v_cantidad_ajustes <> 0 THEN
                    RAISE EXCEPTION
                        'El cambio no declara ajuste de credito, pero existe un ajuste asociado.';
                END IF;
        
                IF v_cambio.valor_ajuste_credito > 0
                   AND (
                       v_cantidad_ajustes <> 1
                       OR v_valor_ajuste <> v_cambio.valor_ajuste_credito
                       OR v_credito_ajuste IS DISTINCT FROM v_cambio.credito_id
                       OR (
                           v_cambio.diferencia > 0 AND v_tipo_ajuste <> 'AUMENTO'
                       )
                       OR (
                           v_cambio.diferencia < 0 AND v_tipo_ajuste <> 'REDUCCION'
                       )
                   )
                THEN
                    RAISE EXCEPTION
                        'El ajuste del credito no coincide con el tratamiento del cambio.';
                END IF;
            END IF;
        
            IF TG_OP = 'DELETE' THEN
                RETURN OLD;
            END IF;
        
            RETURN NEW;
        END;
        $funcion$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(
        "SaleCorrectionsV1 cannot be rolled back: confirmed returns without replacement must remain valid. Use an approved compensating migration.");
}

