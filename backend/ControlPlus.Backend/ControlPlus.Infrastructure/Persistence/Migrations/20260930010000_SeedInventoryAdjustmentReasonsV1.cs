using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20260930010000_SeedInventoryAdjustmentReasonsV1")]
public sealed class SeedInventoryAdjustmentReasonsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO configuracion.motivo_operacion
                (id, establecimiento_id, tipo_operacion, codigo, nombre, descripcion,
                 orden_visual, activo, fecha_creacion, fecha_modificacion, version)
            SELECT
                md5('controlplus.inventory-reason.' || e.id::text || '.' || v.codigo)::uuid,
                e.id,
                'AJUSTE_INVENTARIO',
                v.codigo,
                v.descripcion,
                v.descripcion,
                v.orden_visual,
                true,
                CURRENT_TIMESTAMP,
                NULL,
                1
            FROM configuracion.establecimiento AS e
            CROSS JOIN (VALUES
                ('CONTEO_FISICO', 'Diferencia encontrada al contar existencias.', 10),
                ('MERMA_DANO', 'Producto dañado o no vendible.', 20),
                ('PERDIDA', 'Pérdida o faltante.', 30),
                ('CORRECCION_REGISTRO', 'Corrección documentada de un registro previo.', 40)
            ) AS v(codigo, descripcion, orden_visual)
            ON CONFLICT (establecimiento_id, tipo_operacion, codigo) DO UPDATE
            SET nombre = EXCLUDED.nombre,
                descripcion = EXCLUDED.descripcion,
                orden_visual = EXCLUDED.orden_visual,
                activo = true,
                fecha_modificacion = CURRENT_TIMESTAMP,
                version = configuracion.motivo_operacion.version + 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalog data is intentionally retained. A schema rollback must not delete
        // seeded, preexisting, or already referenced operational reasons.
    }
}
