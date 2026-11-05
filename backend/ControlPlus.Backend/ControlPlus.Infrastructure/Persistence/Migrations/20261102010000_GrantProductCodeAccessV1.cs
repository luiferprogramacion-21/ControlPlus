using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261102010000_GrantProductCodeAccessV1")]
public sealed class GrantProductCodeAccessV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $grant$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
                GRANT SELECT, INSERT ON TABLE catalogo.producto_codigo TO controlplus_app;
                GRANT USAGE ON SEQUENCE catalogo.producto_referencia_seq TO controlplus_app;
                GRANT EXECUTE ON FUNCTION catalogo.siguiente_referencia_producto() TO controlplus_app;
                GRANT EXECUTE ON FUNCTION catalogo.reservar_codigo_producto(text, uuid) TO controlplus_app;
                GRANT EXECUTE ON FUNCTION catalogo.proteger_codigos_producto() TO controlplus_app;
                GRANT EXECUTE ON FUNCTION catalogo.registrar_codigos_producto() TO controlplus_app;
            END IF;
        END $grant$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $revoke$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
                REVOKE SELECT, INSERT ON TABLE catalogo.producto_codigo FROM controlplus_app;
                REVOKE USAGE ON SEQUENCE catalogo.producto_referencia_seq FROM controlplus_app;
                REVOKE EXECUTE ON FUNCTION catalogo.siguiente_referencia_producto() FROM controlplus_app;
                REVOKE EXECUTE ON FUNCTION catalogo.reservar_codigo_producto(text, uuid) FROM controlplus_app;
                REVOKE EXECUTE ON FUNCTION catalogo.proteger_codigos_producto() FROM controlplus_app;
                REVOKE EXECUTE ON FUNCTION catalogo.registrar_codigos_producto() FROM controlplus_app;
            END IF;
        END $revoke$;
        """);
}
