using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261005010000_CustomerSearchIndexesV1")]
public sealed class CustomerSearchIndexesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE INDEX ix_cliente_busqueda_nombre_prefijo
                ON catalogo.cliente USING btree (lower(nombre_completo) text_pattern_ops);

            CREATE INDEX ix_cliente_busqueda_documento_prefijo
                ON catalogo.cliente USING btree (numero_documento varchar_pattern_ops)
                WHERE numero_documento IS NOT NULL;

            CREATE INDEX ix_cliente_busqueda_telefono_prefijo
                ON catalogo.cliente USING btree (lower(telefono) text_pattern_ops)
                WHERE telefono IS NOT NULL;

            CREATE INDEX ix_cliente_activo_nombre_id
                ON catalogo.cliente USING btree (activo, nombre_completo, id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX catalogo.ix_cliente_activo_nombre_id;
            DROP INDEX catalogo.ix_cliente_busqueda_telefono_prefijo;
            DROP INDEX catalogo.ix_cliente_busqueda_documento_prefijo;
            DROP INDEX catalogo.ix_cliente_busqueda_nombre_prefijo;
            """);
    }
}
