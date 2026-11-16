using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Decisión del 16 de noviembre de 2026: al vender a crédito a un cliente moroso o que supera su cupo siempre se avisa,
/// y el Administrador puede activar el bloqueo, con excepción autorizada por un Administrador. Agrega el modo de bloqueo
/// al establecimiento (apagado) y el cupo opcional al cliente (vacío = sin límite). No modifica filas existentes.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261116010000_CreditRiskControlsV1")]
public sealed class CreditRiskControlsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.establecimiento
          ADD COLUMN credito_bloquear_riesgo boolean NOT NULL DEFAULT false;
        COMMENT ON COLUMN configuracion.establecimiento.credito_bloquear_riesgo IS
          'Si es true, vender a credito a un moroso o sobre el cupo exige autorizacion de un Administrador.';
        ALTER TABLE catalogo.cliente
          ADD COLUMN cupo_credito numeric(18,0) NULL;
        ALTER TABLE catalogo.cliente
          ADD CONSTRAINT ck_cliente_cupo_credito CHECK (cupo_credito IS NULL OR cupo_credito > 0);
        COMMENT ON COLUMN catalogo.cliente.cupo_credito IS
          'Saldo maximo que puede deber el cliente en creditos. NULL significa sin limite.';
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE catalogo.cliente DROP CONSTRAINT ck_cliente_cupo_credito;
        ALTER TABLE catalogo.cliente DROP COLUMN cupo_credito;
        ALTER TABLE configuracion.establecimiento DROP COLUMN credito_bloquear_riesgo;
        """);
}
