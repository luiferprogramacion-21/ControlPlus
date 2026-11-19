using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Correcciones operativas (decisión del 19 de noviembre de 2026): «Olvidé cobrar un producto» registra una venta
/// adicional enlazada a la venta de origen. La venta original no se modifica.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261119060000_AdditionalSaleLinkV1")]
public sealed class AdditionalSaleLinkV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE ventas.venta ADD COLUMN venta_origen_id uuid NULL;
        ALTER TABLE ventas.venta
          ADD CONSTRAINT fk_venta_origen FOREIGN KEY (venta_origen_id)
            REFERENCES ventas.venta(id) ON DELETE RESTRICT,
          ADD CONSTRAINT ck_venta_origen_distinta CHECK (venta_origen_id IS NULL OR venta_origen_id <> id);
        CREATE INDEX ix_venta_origen ON ventas.venta(venta_origen_id) WHERE venta_origen_id IS NOT NULL;
        COMMENT ON COLUMN ventas.venta.venta_origen_id IS
          'Venta confirmada a la que se agrega este cobro porque se olvido un producto; la venta de origen no cambia.';
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX ventas.ix_venta_origen;
        ALTER TABLE ventas.venta
          DROP CONSTRAINT ck_venta_origen_distinta,
          DROP CONSTRAINT fk_venta_origen,
          DROP COLUMN venta_origen_id;
        """);
}
