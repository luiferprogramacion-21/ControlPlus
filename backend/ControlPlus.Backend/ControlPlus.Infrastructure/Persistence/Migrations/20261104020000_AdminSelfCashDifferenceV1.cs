using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261104020000_AdminSelfCashDifferenceV1")]
public sealed class AdminSelfCashDifferenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$
        DECLARE original text; changed text;
        BEGIN
            SELECT pg_get_functiondef('caja.vincular_autorizacion_cierre()'::regprocedure) INTO original;
            IF original IS NULL OR position('OR v_autorizacion.usuario_autorizador_id IS NOT DISTINCT FROM NEW.usuario_ejecutor_id' IN original) = 0 THEN
                RAISE EXCEPTION 'La función de vínculo de cierre no coincide con la predecesora aprobada.';
            END IF;
            changed := replace(original,
                'OR v_autorizacion.usuario_autorizador_id IS NOT DISTINCT FROM NEW.usuario_ejecutor_id',
                $replacement$OR (v_autorizacion.usuario_autorizador_id IS NOT DISTINCT FROM NEW.usuario_ejecutor_id
                    AND NOT (seguridad.puede_autoautorizar_administrador(NEW.usuario_ejecutor_id, 'CASH.SHIFT_MANAGE')
                        AND v_autorizacion.descripcion_operacion = 'Autorización de cierre de caja con diferencia por Administrador sin segundo autorizador.'
                        AND v_autorizacion.metodo_autenticacion = 'PASSWORD'
                        AND v_autorizacion.credencial_autorizador_id IS NULL))$replacement$);
            EXECUTE changed;

            SELECT pg_get_functiondef('caja.validar_integridad_arqueo(uuid)'::regprocedure) INTO original;
            IF original IS NULL OR position('AND a.usuario_autorizador_id <> v_turno.usuario_cierre_id' IN original) = 0 THEN
                RAISE EXCEPTION 'La función de integridad de arqueo no coincide con la predecesora aprobada.';
            END IF;
            changed := replace(original,
                'AND a.usuario_autorizador_id <> v_turno.usuario_cierre_id',
                $replacement$AND (a.usuario_autorizador_id <> v_turno.usuario_cierre_id
                    OR (seguridad.puede_autoautorizar_administrador(v_turno.usuario_cierre_id, 'CASH.SHIFT_MANAGE')
                        AND a.descripcion_operacion = 'Autorización de cierre de caja con diferencia por Administrador sin segundo autorizador.'
                        AND a.metodo_autenticacion = 'PASSWORD'
                        AND a.credencial_autorizador_id IS NULL))$replacement$);
            EXECUTE changed;
        END $guard$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $guard$ BEGIN RAISE EXCEPTION 'Admin self authorization requires a reviewed compensating migration'; END $guard$;");
}
