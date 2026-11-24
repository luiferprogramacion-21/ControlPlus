using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Decisiones de la auditoría del usuario del 23 de noviembre de 2026 que tocan la base:
/// D1 — el plazo sugerido de un crédito llega a 180 días o 6 meses calendario (antes 90 días o 3 meses);
/// D8 — la referencia de Nequi y transferencia es opcional también en devoluciones de apartados y en los pagos de cambios
///      de producto (se conserva el rechazo de una referencia escrita en blanco);
/// D7 — el Administrador no tiene porcentaje máximo de descuento: su límite pasa a 100 % y lo que lo frena es el costo.
/// No cambia filas de ventas, apartados, créditos ni pagos.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261124010000_AuditDecisionsV1")]
public sealed class AuditDecisionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_dias_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_dias_credito_predeterminado CHECK (
            dias_credito_predeterminado IS NULL
            OR dias_credito_predeterminado BETWEEN 1 AND 180);
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_meses_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_meses_credito_predeterminado CHECK (
            meses_credito_predeterminado IS NULL
            OR meses_credito_predeterminado BETWEEN 1 AND 6);

        CREATE OR REPLACE FUNCTION ventas.validar_referencia_reembolso()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        BEGIN
            IF NEW.referencia IS NOT NULL AND btrim(NEW.referencia) = '' THEN
                RAISE EXCEPTION 'La referencia opcional no puede estar vacia.';
            END IF;
            RETURN NEW;
        END;
        $funcion$;

        CREATE OR REPLACE FUNCTION ventas.validar_datos_pago_cambio()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        DECLARE
            v_afecta_efectivo boolean;
        BEGIN
            SELECT afecta_efectivo
              INTO v_afecta_efectivo
              FROM catalogo.metodo_pago
             WHERE id = NEW.metodo_pago_id;

            IF NEW.tipo_movimiento = 'DEVOLUCION' THEN
                IF NEW.valor_recibido IS NOT NULL OR NEW.cambio <> 0 THEN
                    RAISE EXCEPTION
                        'Una devolucion no registra valor recibido ni cambio.';
                END IF;
            ELSIF v_afecta_efectivo AND NEW.valor_recibido IS NULL THEN
                RAISE EXCEPTION
                    'El cobro en efectivo requiere registrar el valor recibido.';
            ELSIF NOT v_afecta_efectivo
                  AND (NEW.valor_recibido IS NOT NULL OR NEW.cambio <> 0)
            THEN
                RAISE EXCEPTION
                    'Los cobros distintos de efectivo no usan valor recibido ni cambio.';
            END IF;

            IF NEW.referencia IS NOT NULL AND btrim(NEW.referencia) = '' THEN
                RAISE EXCEPTION 'La referencia opcional no puede estar vacia.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;

        UPDATE seguridad.limite_operacion_rol AS l
           SET valor_maximo = 100, fecha_modificacion = CURRENT_TIMESTAMP, version = l.version + 1
          FROM seguridad.rol AS r
         WHERE r.id = l.rol_id
           AND r.codigo = 'ADMINISTRADOR'
           AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE'
           AND l.tipo_limite = 'PORCENTAJE'
           AND l.valor_maximo <> 100;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          IF EXISTS (SELECT 1 FROM configuracion.establecimiento
                     WHERE dias_credito_predeterminado > 90 OR meses_credito_predeterminado > 3) THEN
            RAISE EXCEPTION 'Hay plazos de crédito mayores a 90 días o 3 meses; se requiere una migración compensatoria';
          END IF;
        END $guard$;
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_dias_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_dias_credito_predeterminado CHECK (
            dias_credito_predeterminado IS NULL
            OR dias_credito_predeterminado BETWEEN 1 AND 90);
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_meses_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_meses_credito_predeterminado CHECK (
            meses_credito_predeterminado IS NULL
            OR meses_credito_predeterminado BETWEEN 1 AND 3);

        CREATE OR REPLACE FUNCTION ventas.validar_referencia_reembolso()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        DECLARE
            v_requiere_referencia boolean;
        BEGIN
            SELECT requiere_referencia
              INTO v_requiere_referencia
              FROM catalogo.metodo_pago
             WHERE id = NEW.metodo_pago_id;

            IF v_requiere_referencia
               AND (NEW.referencia IS NULL OR btrim(NEW.referencia) = '')
            THEN
                RAISE EXCEPTION 'El medio de reembolso requiere una referencia.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;

        CREATE OR REPLACE FUNCTION ventas.validar_datos_pago_cambio()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $funcion$
        DECLARE
            v_afecta_efectivo boolean;
            v_requiere_referencia boolean;
        BEGIN
            SELECT afecta_efectivo, requiere_referencia
              INTO v_afecta_efectivo, v_requiere_referencia
              FROM catalogo.metodo_pago
             WHERE id = NEW.metodo_pago_id;

            IF NEW.tipo_movimiento = 'DEVOLUCION' THEN
                IF NEW.valor_recibido IS NOT NULL OR NEW.cambio <> 0 THEN
                    RAISE EXCEPTION
                        'Una devolucion no registra valor recibido ni cambio.';
                END IF;
            ELSIF v_afecta_efectivo AND NEW.valor_recibido IS NULL THEN
                RAISE EXCEPTION
                    'El cobro en efectivo requiere registrar el valor recibido.';
            ELSIF NOT v_afecta_efectivo
                  AND (NEW.valor_recibido IS NOT NULL OR NEW.cambio <> 0)
            THEN
                RAISE EXCEPTION
                    'Los cobros distintos de efectivo no usan valor recibido ni cambio.';
            END IF;

            IF v_requiere_referencia
               AND (NEW.referencia IS NULL OR btrim(NEW.referencia) = '')
            THEN
                RAISE EXCEPTION 'El medio de pago requiere una referencia.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;

        UPDATE seguridad.limite_operacion_rol AS l
           SET valor_maximo = 80, fecha_modificacion = CURRENT_TIMESTAMP, version = l.version + 1
          FROM seguridad.rol AS r
         WHERE r.id = l.rol_id
           AND r.codigo = 'ADMINISTRADOR'
           AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE'
           AND l.tipo_limite = 'PORCENTAJE';
        """);
}
