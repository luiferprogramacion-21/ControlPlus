using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261115010000_OptionalSalePaymentReferenceV1")]
public sealed class OptionalSalePaymentReferenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER tg_pago_venta_validar_datos ON ventas.pago_venta;

        CREATE FUNCTION ventas.validar_datos_pago_venta()
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

        CREATE TRIGGER tg_pago_venta_validar_datos
        BEFORE INSERT OR UPDATE OF metodo_pago_id, valor_recibido, cambio, referencia
        ON ventas.pago_venta
        FOR EACH ROW EXECUTE FUNCTION ventas.validar_datos_pago_venta();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER tg_pago_venta_validar_datos ON ventas.pago_venta;
        DROP FUNCTION ventas.validar_datos_pago_venta();

        CREATE TRIGGER tg_pago_venta_validar_datos
        BEFORE INSERT OR UPDATE OF metodo_pago_id, valor_recibido, cambio, referencia
        ON ventas.pago_venta
        FOR EACH ROW EXECUTE FUNCTION ventas.validar_datos_cobro();
        """);
}
