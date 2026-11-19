using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ventas en espera (decisión del 19 de noviembre de 2026): el esquema oficial admitía un solo borrador abierto por
/// terminal. Cada cajero puede tener ahora hasta diez ventas sin terminar en el turno (el límite lo aplica la API).
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261119013000_HeldSalesV1")]
public sealed class HeldSalesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX ventas.uq_borrador_venta_activo_terminal;
        CREATE INDEX ix_borrador_venta_abiertos_usuario
          ON ventas.borrador_venta (usuario_id, turno_caja_id)
          WHERE estado IN ('ACTIVO', 'RECUPERADO');
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX ventas.ix_borrador_venta_abiertos_usuario;
        CREATE UNIQUE INDEX uq_borrador_venta_activo_terminal
          ON ventas.borrador_venta (terminal_id)
          WHERE estado IN ('ACTIVO', 'RECUPERADO');
        """);
}
