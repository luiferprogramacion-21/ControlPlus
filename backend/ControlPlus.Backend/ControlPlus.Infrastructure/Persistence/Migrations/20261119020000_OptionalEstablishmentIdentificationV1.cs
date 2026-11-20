using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261119020000_OptionalEstablishmentIdentificationV1")]
public sealed class OptionalEstablishmentIdentificationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE configuracion.establecimiento
                ALTER COLUMN identificacion DROP NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE configuracion.establecimiento
                ALTER COLUMN identificacion SET NOT NULL;
            """);
    }
}
