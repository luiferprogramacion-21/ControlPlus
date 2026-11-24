using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Decisión D3 de la auditoría del 23 de noviembre de 2026: el medio de pago de una venta se corrige aunque la caja de esa
/// venta ya esté cerrada. El cierre no cambia; el reverso y el pago correcto se registran en la caja abierta. Hasta ahora
/// <c>ventas.validar_actor_pago_venta()</c> validaba todo pago nuevo contra el turno de la venta, que ya no está abierto.
/// Solo cambia esa función: el pago se valida contra el turno de su movimiento de caja cuando ese movimiento pertenece a
/// una corrección de esta misma venta (lo acompaña, con la misma correlación y en el mismo turno, el reverso REVERSO_PAGO
/// del movimiento original de la venta). En cualquier otro caso sigue la regla oficial. No cambia filas.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261124020000_ClosedShiftPaymentCorrectionV1")]
public sealed class ClosedShiftPaymentCorrectionV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION ventas.validar_actor_pago_venta()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        DECLARE
            v_turno_caja_id uuid;
            v_turno_movimiento uuid;
            v_correlacion uuid;
        BEGIN
            SELECT turno_caja_id
              INTO v_turno_caja_id
              FROM ventas.venta
             WHERE id = NEW.venta_id;

            -- Corrección del medio de pago de una venta de un turno ya cerrado (D3): el pago vive en el turno abierto.
            IF NEW.movimiento_caja_id IS NOT NULL THEN
                SELECT turno_caja_id, correlacion_id
                  INTO v_turno_movimiento, v_correlacion
                  FROM caja.movimiento_caja
                 WHERE id = NEW.movimiento_caja_id;

                IF v_turno_movimiento IS DISTINCT FROM v_turno_caja_id
                   AND EXISTS (
                       SELECT 1
                         FROM caja.movimiento_caja r
                         JOIN ventas.pago_venta p ON p.movimiento_caja_id = r.movimiento_revertido_id
                        WHERE r.correlacion_id = v_correlacion
                          AND r.turno_caja_id = v_turno_movimiento
                          AND r.tipo = 'REVERSO'
                          AND r.categoria_movimiento = 'REVERSO_PAGO'
                          AND p.venta_id = NEW.venta_id)
                THEN
                    v_turno_caja_id := v_turno_movimiento;
                END IF;
            END IF;

            IF NOT caja.actor_valido_turno(
                v_turno_caja_id,
                NEW.usuario_id,
                NEW.sesion_operador_id
            )
            THEN
                RAISE EXCEPTION
                    'El operador o la sesion no corresponden al pago de la venta.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION ventas.validar_actor_pago_venta()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        DECLARE
            v_turno_caja_id uuid;
        BEGIN
            SELECT turno_caja_id
              INTO v_turno_caja_id
              FROM ventas.venta
             WHERE id = NEW.venta_id;

            IF NOT caja.actor_valido_turno(
                v_turno_caja_id,
                NEW.usuario_id,
                NEW.sesion_operador_id
            )
            THEN
                RAISE EXCEPTION
                    'El operador o la sesion no corresponden al pago de la venta.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;
        """);
}
