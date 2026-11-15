using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Decisión del 15 de noviembre de 2026: el Administrador configura cuántas unidades de un mismo producto exige el precio
/// mayorista en ventas, apartados y cambios. El valor inicial 12 conserva la regla fija anterior; no modifica filas
/// históricas ni documentos ya emitidos.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261115020000_ConfigurableWholesaleMinimumV1")]
public sealed class ConfigurableWholesaleMinimumV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          ADD COLUMN minimo_unidades_mayorista integer NOT NULL DEFAULT 12;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_minimo_unidades_mayorista CHECK (
            minimo_unidades_mayorista BETWEEN 1 AND 999);
        COMMENT ON COLUMN configuracion.establecimiento.minimo_unidades_mayorista IS
          'Unidades minimas de un mismo producto para vender a precio mayorista en ventas, apartados y cambios.';
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_minimo_unidades_mayorista;
        ALTER TABLE configuracion.establecimiento
          DROP COLUMN minimo_unidades_mayorista;
        """);
}
