using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261111010000_BusinessVatWholePesoLayawaysChangesV1")]
public sealed class BusinessVatWholePesoLayawaysChangesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE ventas.detalle_apartado DROP CONSTRAINT ck_detalle_apartado_iva_incluido;
        ALTER TABLE ventas.detalle_apartado ADD CONSTRAINT ck_detalle_apartado_iva_incluido CHECK (
            (porcentaje_iva IS NULL AND base_gravable IS NULL AND valor_iva IS NULL)
            OR (porcentaje_iva IS NOT NULL AND base_gravable IS NOT NULL AND base_gravable >= 0
                AND valor_iva IS NOT NULL AND valor_iva >= 0
                AND (base_gravable = round(subtotal_neto * 100 / (100 + porcentaje_iva), 4)
                    OR base_gravable = round(subtotal_neto * 100 / (100 + porcentaje_iva), 0))
                AND base_gravable + valor_iva = subtotal_neto));

        ALTER TABLE ventas.detalle_cambio_venta DROP CONSTRAINT ck_detalle_cambio_iva_incluido;
        ALTER TABLE ventas.detalle_cambio_venta ADD CONSTRAINT ck_detalle_cambio_iva_incluido CHECK (
            (porcentaje_iva IS NULL AND base_gravable IS NULL AND valor_iva IS NULL)
            OR (porcentaje_iva IS NOT NULL AND base_gravable IS NOT NULL AND base_gravable >= 0
                AND valor_iva IS NOT NULL AND valor_iva >= 0
                AND (base_gravable = round(subtotal_neto * 100 / (100 + porcentaje_iva), 4)
                    OR base_gravable = round(subtotal_neto * 100 / (100 + porcentaje_iva), 0))
                AND base_gravable + valor_iva = subtotal_neto));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          RAISE EXCEPTION 'La reversión requiere evaluar apartados y cambios con base redondeada a COP entero';
        END $guard$;
        """);
}
