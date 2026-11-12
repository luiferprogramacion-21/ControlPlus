using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261112010000_LayawayMinimumInitialPaymentV1")]
public sealed class LayawayMinimumInitialPaymentV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          ADD COLUMN apartado_exige_minimo boolean NOT NULL DEFAULT true;

        ALTER TABLE configuracion.establecimiento
          ADD COLUMN dias_credito_predeterminado integer NULL DEFAULT 30;
        ALTER TABLE configuracion.establecimiento
          ADD CONSTRAINT ck_establecimiento_dias_credito_predeterminado CHECK (
            dias_credito_predeterminado IS NULL
            OR dias_credito_predeterminado BETWEEN 1 AND 90);

        ALTER TABLE ventas.apartado DROP CONSTRAINT ck_apartado_montos;
        ALTER TABLE ventas.apartado ADD CONSTRAINT ck_apartado_montos CHECK (
            subtotal_bruto >= 0
            AND descuento_total >= 0
            AND descuento_total <= subtotal_bruto
            AND total > 0
            AND total = subtotal_bruto - descuento_total
            AND porcentaje_minimo_inicial >= 0
            AND porcentaje_minimo_inicial <= 100
            AND abono_inicial >= CASE
                WHEN porcentaje_minimo_inicial = 0 THEN 1
                ELSE ceil(total * porcentaje_minimo_inicial / 100)
            END
            AND abono_inicial <= total
            AND saldo_pendiente >= 0
            AND saldo_pendiente <= total);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
          IF EXISTS (SELECT 1 FROM ventas.apartado WHERE porcentaje_minimo_inicial = 0) THEN
            RAISE EXCEPTION 'Existen apartados creados sin porcentaje mínimo; se requiere una migración compensatoria';
          END IF;
        END $guard$;

        ALTER TABLE ventas.apartado DROP CONSTRAINT ck_apartado_montos;
        ALTER TABLE ventas.apartado ADD CONSTRAINT ck_apartado_montos CHECK (
            subtotal_bruto >= 0
            AND descuento_total >= 0
            AND descuento_total <= subtotal_bruto
            AND total > 0
            AND total = subtotal_bruto - descuento_total
            AND porcentaje_minimo_inicial > 0
            AND porcentaje_minimo_inicial <= 100
            AND abono_inicial >= round(total * porcentaje_minimo_inicial / 100, 0)
            AND abono_inicial <= total
            AND saldo_pendiente >= 0
            AND saldo_pendiente <= total);

        ALTER TABLE configuracion.establecimiento
          DROP CONSTRAINT ck_establecimiento_dias_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento DROP COLUMN dias_credito_predeterminado;
        ALTER TABLE configuracion.establecimiento DROP COLUMN apartado_exige_minimo;
        """);
}
