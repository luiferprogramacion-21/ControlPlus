using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261102020000_CategoryProductReferencesV1")]
public sealed class CategoryProductReferencesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE catalogo.categoria ADD COLUMN abreviatura varchar(10) NULL;
        ALTER TABLE catalogo.categoria ADD CONSTRAINT ck_categoria_abreviatura
            CHECK (abreviatura IS NULL OR abreviatura ~ '^[A-Z]{2,10}$');
        CREATE UNIQUE INDEX uq_categoria_abreviatura ON catalogo.categoria(abreviatura);

        CREATE FUNCTION catalogo.siguiente_referencia_producto(category_id uuid) RETURNS text LANGUAGE plpgsql AS $fn$
        DECLARE prefix text; number bigint; suffix text; candidate text;
        BEGIN
            PERFORM pg_advisory_xact_lock(1129336912, 1);
            SELECT abreviatura INTO prefix FROM catalogo.categoria WHERE id = category_id AND activo FOR SHARE;
            IF prefix IS NULL THEN
                RAISE EXCEPTION 'Category abbreviation is required' USING ERRCODE = '23514';
            END IF;
            LOOP
                number := nextval('catalogo.producto_referencia_seq');
                suffix := lpad(number::text, greatest(6, length(number::text)), '0');
                candidate := prefix || '-' || suffix;
                IF NOT EXISTS (SELECT 1 FROM catalogo.producto_codigo WHERE codigo IN (candidate, 'CP' || suffix))
                   AND NOT EXISTS (SELECT 1 FROM catalogo.producto WHERE codigo_interno = candidate OR codigo_barras = 'CP' || suffix)
                THEN RETURN candidate; END IF;
            END LOOP;
        END $fn$;

        DO $grant$ BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
                GRANT EXECUTE ON FUNCTION catalogo.siguiente_referencia_producto(uuid) TO controlplus_app;
            END IF;
        END $grant$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $guard$ BEGIN RAISE EXCEPTION 'Category references require a reviewed compensating migration'; END $guard$;");
}
