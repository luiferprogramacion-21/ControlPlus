using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Decisión del 16 de noviembre de 2026: pago único o cuotas opcionales. Conserva cada crédito previo como una
/// cuota única y registra la aplicación de los abonos vigentes para permitir anulaciones exactas.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261116020000_CreditInstallmentsV1")]
public sealed class CreditInstallmentsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE ventas.cuota_credito (
          id uuid PRIMARY KEY,
          credito_id uuid NOT NULL,
          numero integer NOT NULL,
          monto numeric(18,0) NOT NULL,
          saldo_pendiente numeric(18,0) NOT NULL,
          fecha_vencimiento date NOT NULL,
          fecha_pago date NULL,
          CONSTRAINT fk_cuota_credito_credito FOREIGN KEY (credito_id)
            REFERENCES ventas.credito(id) ON DELETE CASCADE,
          CONSTRAINT ck_cuota_credito_numero CHECK (numero > 0),
          CONSTRAINT ck_cuota_credito_montos CHECK (
            monto >= 0 AND saldo_pendiente >= 0 AND saldo_pendiente <= monto),
          CONSTRAINT uq_cuota_credito_numero UNIQUE (credito_id, numero)
        );
        CREATE INDEX ix_cuota_credito_vencimiento_saldo
          ON ventas.cuota_credito(fecha_vencimiento, saldo_pendiente);

        CREATE TABLE ventas.aplicacion_pago_cuota (
          id uuid PRIMARY KEY,
          pago_credito_id uuid NOT NULL,
          cuota_credito_id uuid NOT NULL,
          valor_aplicado numeric(18,0) NOT NULL,
          CONSTRAINT fk_aplicacion_pago_cuota_pago FOREIGN KEY (pago_credito_id)
            REFERENCES ventas.pago_credito(id) ON DELETE RESTRICT,
          CONSTRAINT fk_aplicacion_pago_cuota_cuota FOREIGN KEY (cuota_credito_id)
            REFERENCES ventas.cuota_credito(id) ON DELETE RESTRICT,
          CONSTRAINT ck_aplicacion_pago_cuota_valor CHECK (valor_aplicado > 0),
          CONSTRAINT uq_aplicacion_pago_cuota UNIQUE (pago_credito_id, cuota_credito_id)
        );
        CREATE INDEX ix_aplicacion_pago_cuota_cuota
          ON ventas.aplicacion_pago_cuota(cuota_credito_id);

        INSERT INTO ventas.cuota_credito
          (id, credito_id, numero, monto, saldo_pendiente, fecha_vencimiento, fecha_pago)
        SELECT (substr(md5(c.id::text || ':cuota:1'),1,8) || '-' ||
                substr(md5(c.id::text || ':cuota:1'),9,4) || '-' ||
                substr(md5(c.id::text || ':cuota:1'),13,4) || '-' ||
                substr(md5(c.id::text || ':cuota:1'),17,4) || '-' ||
                substr(md5(c.id::text || ':cuota:1'),21,12))::uuid,
               c.id, 1, c.monto_ajustado, c.saldo_pendiente, c.fecha_vencimiento,
               CASE WHEN c.estado = 'PAGADO' THEN c.fecha_pago ELSE NULL END
        FROM ventas.credito c;

        INSERT INTO ventas.aplicacion_pago_cuota(id, pago_credito_id, cuota_credito_id, valor_aplicado)
        SELECT (substr(md5(p.id::text || ':aplicacion'),1,8) || '-' ||
                substr(md5(p.id::text || ':aplicacion'),9,4) || '-' ||
                substr(md5(p.id::text || ':aplicacion'),13,4) || '-' ||
                substr(md5(p.id::text || ':aplicacion'),17,4) || '-' ||
                substr(md5(p.id::text || ':aplicacion'),21,12))::uuid,
               p.id, q.id, p.valor_aplicado
        FROM ventas.pago_credito p
        JOIN ventas.cuota_credito q ON q.credito_id = p.credito_id AND q.numero = 1
        WHERE p.estado = 'APLICADO';

        DO $grant$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
            GRANT SELECT, INSERT, UPDATE, DELETE ON ventas.cuota_credito TO controlplus_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ventas.aplicacion_pago_cuota TO controlplus_app;
          END IF;
        END $grant$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE ventas.aplicacion_pago_cuota;
        DROP TABLE ventas.cuota_credito;
        """);
}
