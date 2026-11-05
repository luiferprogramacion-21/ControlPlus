using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261104010000_AdminSelfSaleCancellationV1")]
public sealed class AdminSelfSaleCancellationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION seguridad.puede_autoautorizar_administrador(p_usuario uuid, p_permiso text)
        RETURNS boolean LANGUAGE sql STABLE AS $fn$
            SELECT EXISTS (
                SELECT 1
                FROM seguridad.usuario u
                JOIN seguridad.usuario_rol ur ON ur.usuario_id = u.id
                JOIN seguridad.rol r ON r.id = ur.rol_id
                JOIN seguridad.permiso p ON p.codigo = p_permiso AND p.activo
                WHERE u.id = p_usuario AND u.activo AND u.bloqueo_hasta IS NULL
                  AND r.activo AND r.codigo = 'ADMINISTRADOR'
                  AND NOT EXISTS (SELECT 1 FROM seguridad.usuario_permiso up
                                  WHERE up.usuario_id = u.id AND up.permiso_id = p.id AND up.efecto = 'REVOCAR')
                  AND (EXISTS (SELECT 1 FROM seguridad.usuario_permiso up
                               WHERE up.usuario_id = u.id AND up.permiso_id = p.id AND up.efecto = 'CONCEDER')
                       OR EXISTS (SELECT 1 FROM seguridad.rol_permiso rp
                                  WHERE rp.rol_id = r.id AND rp.permiso_id = p.id))
            );
        $fn$;

        ALTER TABLE ventas.anulacion_venta DROP CONSTRAINT ck_anulacion_venta_usuarios;
        CREATE FUNCTION ventas.validar_admin_autoanulacion()
        RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            IF NEW.usuario_solicitante_id = NEW.usuario_autorizador_id AND NOT EXISTS (
                SELECT 1 FROM seguridad.autorizacion_operacion a
                WHERE a.id = NEW.autorizacion_operacion_id
                  AND a.usuario_solicitante_id = NEW.usuario_solicitante_id
                  AND a.usuario_autorizador_id = NEW.usuario_autorizador_id
                  AND a.tipo_operacion = 'ANULACION_VENTA'
                  AND a.descripcion_operacion = 'Autorización de anulación de venta por Administrador sin segundo autorizador.'
                  AND a.metodo_autenticacion = 'PASSWORD'
                  AND a.credencial_autorizador_id IS NULL
                  AND a.estado = 'UTILIZADA' AND a.resultado = 'EXITOSA'
                  AND a.fecha_utilizacion IS NOT NULL
                  AND seguridad.puede_autoautorizar_administrador(NEW.usuario_solicitante_id, 'SALES.VOID')
            ) THEN
                RAISE EXCEPTION 'La autoautorización de anulación de venta no es válida.' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $fn$;
        CREATE TRIGGER tg_anulacion_venta_validar_admin
        BEFORE INSERT OR UPDATE OF usuario_solicitante_id, usuario_autorizador_id, autorizacion_operacion_id
        ON ventas.anulacion_venta FOR EACH ROW EXECUTE FUNCTION ventas.validar_admin_autoanulacion();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $guard$ BEGIN RAISE EXCEPTION 'Admin self authorization requires a reviewed compensating migration'; END $guard$;");
}
