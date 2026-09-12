using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20260912010000_OfficialPhase4Baseline")]
public sealed class OfficialPhase4Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OfficialDatabaseScript.Load(), suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP SCHEMA IF EXISTS auditoria, compras, ventas, caja, catalogo, configuracion, seguridad CASCADE;",
            suppressTransaction: true);
    }
}
