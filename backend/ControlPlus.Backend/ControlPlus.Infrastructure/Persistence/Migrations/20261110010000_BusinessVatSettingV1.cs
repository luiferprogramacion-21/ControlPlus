using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261110010000_BusinessVatSettingV1")]
public sealed class BusinessVatSettingV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          ADD COLUMN iva_negocio_habilitado boolean NOT NULL DEFAULT false,
          ADD COLUMN iva_negocio_porcentaje numeric(5,2) NULL,
          ADD COLUMN iva_negocio_version bigint NOT NULL DEFAULT 0,
          ADD COLUMN iva_negocio_actualizado_utc timestamptz NULL,
          ADD CONSTRAINT ck_establecimiento_iva_negocio CHECK (
            iva_negocio_version >= 0 AND
            (iva_negocio_porcentaje IS NULL OR iva_negocio_porcentaje > 0 AND iva_negocio_porcentaje <= 100) AND
            (NOT iva_negocio_habilitado OR iva_negocio_porcentaje IS NOT NULL));

        INSERT INTO seguridad.permiso (id, codigo, nombre, descripcion, modulo)
        SELECT md5('controlplus.permission.TAXES.MANAGE')::uuid, 'TAXES.MANAGE',
               'Gestionar IVA', 'Configuración de IVA del negocio.', 'CONFIGURACION'
        WHERE NOT EXISTS (SELECT 1 FROM seguridad.permiso WHERE codigo = 'TAXES.MANAGE');

        INSERT INTO seguridad.rol_permiso (rol_id, permiso_id)
        SELECT r.id, p.id FROM seguridad.rol r CROSS JOIN seguridad.permiso p
        WHERE r.codigo = 'ADMINISTRADOR' AND p.codigo = 'TAXES.MANAGE'
        ON CONFLICT DO NOTHING;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          RAISE EXCEPTION 'El ajuste de IVA requiere una migración compensatoria revisada';
        END $guard$;
        """);
}
