using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261106010000_ProductPhysicalDeletionPrivilegesV1")]
public sealed class ProductPhysicalDeletionPrivilegesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $grant$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
                GRANT DELETE ON TABLE catalogo.producto_codigo TO controlplus_app;
                GRANT DELETE ON TABLE catalogo.producto TO controlplus_app;
                GRANT DELETE ON TABLE compras.movimiento_inventario TO controlplus_app;
            END IF;
        END $grant$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $revoke$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
                REVOKE DELETE ON TABLE catalogo.producto_codigo FROM controlplus_app;
                REVOKE DELETE ON TABLE catalogo.producto FROM controlplus_app;
                REVOKE DELETE ON TABLE compras.movimiento_inventario FROM controlplus_app;
            END IF;
        END $revoke$;
        """);
}
