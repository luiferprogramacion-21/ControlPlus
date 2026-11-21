using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Propuesta 7 aprobada el 21 de noviembre de 2026: la referencia de Transferencia y Nequi pasa a ser opcional también en
/// abonos de crédito y pagos de apartado, como en las ventas desde el 15 de noviembre de 2026.
/// Desde <c>OptionalSalePaymentReferenceV1</c> la función compartida <c>ventas.validar_datos_cobro()</c> solo la usan los
/// triggers de <c>ventas.pago_credito</c> y <c>ventas.pago_apartado</c>; se reemplaza su cuerpo sin tocar los triggers ni
/// ninguna fila. Se conservan el valor recibido obligatorio del efectivo, la prohibición de valor recibido y cambio en los
/// demás medios y el rechazo de una referencia vacía.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261121010000_OptionalCreditLayawayPaymentReferenceV1")]
public sealed class OptionalCreditLayawayPaymentReferenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION ventas.validar_datos_cobro()
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

            IF v_afecta_efectivo THEN
                IF NEW.valor_recibido IS NULL THEN
                    RAISE EXCEPTION
                        'El pago en efectivo requiere registrar el valor recibido.';
                END IF;
            ELSIF NEW.valor_recibido IS NOT NULL OR NEW.cambio <> 0 THEN
                RAISE EXCEPTION
                    'Los medios distintos de efectivo no usan valor recibido ni cambio.';
            END IF;

            IF NEW.referencia IS NOT NULL AND btrim(NEW.referencia) = '' THEN
                RAISE EXCEPTION 'La referencia opcional no puede estar vacia.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION ventas.validar_datos_cobro()
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

            IF v_afecta_efectivo THEN
                IF NEW.valor_recibido IS NULL THEN
                    RAISE EXCEPTION
                        'El pago en efectivo requiere registrar el valor recibido.';
                END IF;
            ELSIF NEW.valor_recibido IS NOT NULL OR NEW.cambio <> 0 THEN
                RAISE EXCEPTION
                    'Los medios distintos de efectivo no usan valor recibido ni cambio.';
            END IF;

            IF v_requiere_referencia
               AND (NEW.referencia IS NULL OR btrim(NEW.referencia) = '')
            THEN
                RAISE EXCEPTION 'El medio de pago requiere una referencia.';
            END IF;

            IF NOT v_requiere_referencia AND NEW.referencia IS NOT NULL
               AND btrim(NEW.referencia) = ''
            THEN
                RAISE EXCEPTION 'La referencia opcional no puede estar vacia.';
            END IF;

            RETURN NEW;
        END;
        $funcion$;
        """);
}
