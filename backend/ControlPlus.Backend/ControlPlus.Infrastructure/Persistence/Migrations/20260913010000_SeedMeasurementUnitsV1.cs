using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20260913010000_SeedMeasurementUnitsV1")]
public sealed class SeedMeasurementUnitsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO catalogo.unidad_medida (id, codigo, nombre, simbolo, activo)
            VALUES
                (md5('controlplus.measurement-unit.UNIDAD')::uuid, 'UNIDAD', 'Unidad', 'und', true),
                (md5('controlplus.measurement-unit.PAQUETE')::uuid, 'PAQUETE', 'Paquete', 'paq', true),
                (md5('controlplus.measurement-unit.METRO')::uuid, 'METRO', 'Metro', 'm', true)
            ON CONFLICT (codigo) DO UPDATE
            SET nombre = EXCLUDED.nombre,
                simbolo = EXCLUDED.simbolo,
                activo = true;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM catalogo.unidad_medida
            WHERE id IN (
                md5('controlplus.measurement-unit.UNIDAD')::uuid,
                md5('controlplus.measurement-unit.PAQUETE')::uuid,
                md5('controlplus.measurement-unit.METRO')::uuid);
            """);
    }
}
