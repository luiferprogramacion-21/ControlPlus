using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261028010000_SaleReceiptDataSnapshotV1")]
public sealed class SaleReceiptDataSnapshotV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "ALTER TABLE configuracion.documento_emitido " +
            "ALTER COLUMN plantilla_snapshot DROP NOT NULL;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            "ALTER TABLE configuracion.documento_emitido " +
            "ALTER COLUMN plantilla_snapshot SET NOT NULL;");
}
