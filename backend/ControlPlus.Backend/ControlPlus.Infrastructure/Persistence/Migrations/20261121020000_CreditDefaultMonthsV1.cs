using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Permite que el plazo sugerido de un crédito nuevo se configure en meses calendario.
/// Conserva sin cambios el valor anterior de días y todos los créditos existentes.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261121020000_CreditDefaultMonthsV1")]
public sealed class CreditDefaultMonthsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          ADD COLUMN meses_credito_predeterminado integer NULL;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_meses_credito_predeterminado CHECK (
            meses_credito_predeterminado IS NULL
            OR meses_credito_predeterminado BETWEEN 1 AND 3);
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_plazo_credito_exclusivo CHECK (
            dias_credito_predeterminado IS NULL
            OR meses_credito_predeterminado IS NULL);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          IF EXISTS (SELECT 1 FROM configuracion.establecimiento
                     WHERE meses_credito_predeterminado IS NOT NULL) THEN
            RAISE EXCEPTION 'Hay plazos de crédito en meses; se requiere una migración compensatoria';
          END IF;
        END $guard$;
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_plazo_credito_exclusivo;
        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_meses_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento
          DROP COLUMN meses_credito_predeterminado;
        """);
}
