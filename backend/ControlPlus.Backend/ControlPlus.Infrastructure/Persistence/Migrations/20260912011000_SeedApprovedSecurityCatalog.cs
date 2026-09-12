using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20260912011000_SeedApprovedSecurityCatalog")]
public sealed class SeedApprovedSecurityCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO seguridad.rol
                (id, codigo, nombre, descripcion, es_predefinido, permisos_editables, activo)
            VALUES
                ('00000000-0000-7000-8000-000000000001', 'ADMINISTRADOR', 'Administrador', 'Plantilla inicial aprobada.', true, true, true),
                ('00000000-0000-7000-8000-000000000002', 'SUPERVISOR', 'Supervisor', 'Plantilla inicial aprobada.', true, true, true),
                ('00000000-0000-7000-8000-000000000003', 'CAJERO', 'Cajero', 'Plantilla inicial aprobada.', true, true, true)
            ON CONFLICT (codigo) DO NOTHING;

            INSERT INTO seguridad.limite_operacion_rol
                (id, rol_id, codigo_operacion, tipo_limite, valor_maximo)
            SELECT seed.id, rol.id, 'DESCUENTO_PORCENTAJE', 'PORCENTAJE', seed.valor
            FROM (VALUES
                ('00000000-0000-7000-8000-000000000011'::uuid, 'ADMINISTRADOR', 100.0000::numeric),
                ('00000000-0000-7000-8000-000000000012'::uuid, 'SUPERVISOR', 20.0000::numeric),
                ('00000000-0000-7000-8000-000000000013'::uuid, 'CAJERO', 5.0000::numeric)
            ) AS seed(id, codigo_rol, valor)
            JOIN seguridad.rol rol ON rol.codigo = seed.codigo_rol
            ON CONFLICT (rol_id, codigo_operacion) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM seguridad.limite_operacion_rol
            WHERE id IN (
                '00000000-0000-7000-8000-000000000011',
                '00000000-0000-7000-8000-000000000012',
                '00000000-0000-7000-8000-000000000013');

            DELETE FROM seguridad.rol
            WHERE id IN (
                '00000000-0000-7000-8000-000000000001',
                '00000000-0000-7000-8000-000000000002',
                '00000000-0000-7000-8000-000000000003');
            """);
    }
}
