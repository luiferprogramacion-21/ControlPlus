using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261110020000_BusinessVatWholePesoSalesV1")]
public sealed class BusinessVatWholePesoSalesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- The official constraint accepted four decimal places only. Preserve those historical
        -- rows while allowing the approved whole COP base for new sales.
        ALTER TABLE ventas.detalle_venta DROP CONSTRAINT ck_detalle_venta_iva_incluido;
        ALTER TABLE ventas.detalle_venta ADD CONSTRAINT ck_detalle_venta_iva_incluido CHECK (
            (porcentaje_iva IS NULL AND base_gravable IS NULL AND valor_iva IS NULL)
            OR
            (porcentaje_iva IS NOT NULL
             AND base_gravable IS NOT NULL AND base_gravable >= 0
             AND valor_iva IS NOT NULL AND valor_iva >= 0
             AND (base_gravable = round(subtotal_neto * 100 / (100 + porcentaje_iva), 4)
                  OR base_gravable = round(subtotal_neto * 100 / (100 + porcentaje_iva), 0))
             AND base_gravable + valor_iva = subtotal_neto)
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          RAISE EXCEPTION 'La reversión requiere evaluar ventas con base gravable redondeada a COP entero';
        END $guard$;
        """);
}
