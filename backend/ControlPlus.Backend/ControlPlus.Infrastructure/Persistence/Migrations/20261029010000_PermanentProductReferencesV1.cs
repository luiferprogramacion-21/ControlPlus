using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261029010000_PermanentProductReferencesV1")]
public sealed class PermanentProductReferencesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE catalogo.producto IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE catalogo.producto DROP CONSTRAINT ck_producto_formato_codigo;
        ALTER TABLE catalogo.producto ADD CONSTRAINT ck_producto_formato_codigo CHECK (
            (codigo_barras IS NULL AND formato_codigo_barras IS NULL AND NOT codigo_barras_generado)
            OR (codigo_barras IS NOT NULL AND (
                (codigo_barras_generado AND formato_codigo_barras IS NOT NULL
                    AND formato_codigo_barras IN ('CODE128', 'EAN13', 'EAN8', 'UPC_A', 'OTRO'))
                OR (NOT codigo_barras_generado AND (formato_codigo_barras IS NULL
                    OR formato_codigo_barras IN ('CODE128', 'EAN13', 'EAN8', 'UPC_A', 'OTRO')))
            ))
        );
        CREATE TABLE catalogo.producto_codigo (
            codigo text COLLATE "C" PRIMARY KEY,
            producto_id uuid NOT NULL REFERENCES catalogo.producto(id) ON DELETE RESTRICT,
            fecha_registro timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT ck_producto_codigo_normalizado CHECK (codigo <> '' AND codigo = upper(btrim(codigo)))
        );
        CREATE INDEX ix_producto_codigo_producto ON catalogo.producto_codigo(producto_id);

        -- Abort atomically if old references/barcodes already identify different products.
        -- Snapshots, document details and audit records are deliberately not rewritten.
        INSERT INTO catalogo.producto_codigo(codigo, producto_id)
        SELECT DISTINCT upper(btrim(codigo)), id FROM (
            SELECT codigo_interno AS codigo, id FROM catalogo.producto
            UNION ALL SELECT codigo_barras, id FROM catalogo.producto WHERE codigo_barras IS NOT NULL
        ) codes WHERE btrim(codigo) <> '';

        CREATE SEQUENCE catalogo.producto_referencia_seq AS bigint MINVALUE 1 NO CYCLE;
        CREATE FUNCTION catalogo.siguiente_referencia_producto() RETURNS text LANGUAGE plpgsql AS $fn$
        DECLARE number bigint; candidate text;
        BEGIN
            PERFORM pg_advisory_xact_lock(1129336912, 1);
            LOOP
                number := nextval('catalogo.producto_referencia_seq');
                candidate := 'P-' || lpad(number::text, greatest(6, length(number::text)), '0');
                IF NOT EXISTS (SELECT 1 FROM catalogo.producto_codigo WHERE codigo = candidate) THEN
                    RETURN candidate;
                END IF;
            END LOOP;
        END $fn$;

        DO $migration$
        DECLARE item record; reference text;
        BEGIN
            FOR item IN SELECT id FROM catalogo.producto ORDER BY fecha_creacion, id LOOP
                reference := catalogo.siguiente_referencia_producto();
                UPDATE catalogo.producto SET codigo_interno = reference, version = version + 1 WHERE id = item.id;
                INSERT INTO catalogo.producto_codigo(codigo, producto_id) VALUES(reference, item.id);
            END LOOP;
        END $migration$;

        CREATE FUNCTION catalogo.reservar_codigo_producto(code text, product uuid) RETURNS void LANGUAGE plpgsql AS $fn$
        DECLARE normalized text := upper(btrim(code));
        BEGIN
            IF normalized IS NULL OR normalized = '' THEN RETURN; END IF;
            PERFORM pg_advisory_xact_lock(1129336912, 1);
            INSERT INTO catalogo.producto_codigo(codigo, producto_id) VALUES(normalized, product)
                ON CONFLICT(codigo) DO NOTHING;
            IF NOT EXISTS (SELECT 1 FROM catalogo.producto_codigo WHERE codigo = normalized AND producto_id = product) THEN
                RAISE EXCEPTION 'Product lookup code is already reserved'
                    USING ERRCODE = '23505', CONSTRAINT = 'uq_producto_codigo_global';
            END IF;
        END $fn$;

        CREATE FUNCTION catalogo.proteger_codigos_producto() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            PERFORM pg_advisory_xact_lock(1129336912, 1);
            IF TG_OP = 'UPDATE' AND NEW.codigo_interno IS DISTINCT FROM OLD.codigo_interno THEN
                RAISE EXCEPTION 'Product reference is permanent' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $fn$;
        CREATE TRIGGER trg_producto_referencia_permanente BEFORE INSERT OR UPDATE OF codigo_interno, codigo_barras
            ON catalogo.producto FOR EACH ROW EXECUTE FUNCTION catalogo.proteger_codigos_producto();

        CREATE FUNCTION catalogo.registrar_codigos_producto() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            PERFORM catalogo.reservar_codigo_producto(NEW.codigo_interno, NEW.id);
            PERFORM catalogo.reservar_codigo_producto(NEW.codigo_barras, NEW.id);
            RETURN NEW;
        END $fn$;
        CREATE TRIGGER trg_producto_registrar_codigos AFTER INSERT OR UPDATE OF codigo_interno, codigo_barras
            ON catalogo.producto FOR EACH ROW EXECUTE FUNCTION catalogo.registrar_codigos_producto();
        """);

    // References and historical aliases must never be silently renumbered or released.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $guard$ BEGIN RAISE EXCEPTION 'Permanent product references require a reviewed compensating migration'; END $guard$;");
}
