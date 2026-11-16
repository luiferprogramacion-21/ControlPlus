-- Restauración de emergencia: vuelve a conceder a controlplus_app los permisos que las migraciones dieron sobre objetos
-- cuyo dueño es postgres. Solo se usa cuando la copia restaurada no trae permisos (copias anteriores al 15 de noviembre
-- de 2026, hechas con --no-acl). Cada concesión depende de que su migración figure en el historial de la base
-- restaurada y de que el objeto exista; no crea ni modifica datos.
DO $$
DECLARE
    applied text[];
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
        RAISE EXCEPTION 'No existe el rol controlplus_app.';
    END IF;
    SELECT coalesce(array_agg("MigrationId"), ARRAY[]::text[]) INTO applied FROM public."__EFMigrationsHistory";

    -- 20261102010000_GrantProductCodeAccessV1
    IF '20261102010000_GrantProductCodeAccessV1' = ANY (applied) THEN
        GRANT SELECT, INSERT ON TABLE catalogo.producto_codigo TO controlplus_app;
        GRANT USAGE ON SEQUENCE catalogo.producto_referencia_seq TO controlplus_app;
        GRANT EXECUTE ON FUNCTION catalogo.siguiente_referencia_producto() TO controlplus_app;
        GRANT EXECUTE ON FUNCTION catalogo.reservar_codigo_producto(text, uuid) TO controlplus_app;
        GRANT EXECUTE ON FUNCTION catalogo.proteger_codigos_producto() TO controlplus_app;
        GRANT EXECUTE ON FUNCTION catalogo.registrar_codigos_producto() TO controlplus_app;
    END IF;

    -- 20261102020000_CategoryProductReferencesV1
    IF '20261102020000_CategoryProductReferencesV1' = ANY (applied)
       AND to_regprocedure('catalogo.siguiente_referencia_producto(uuid)') IS NOT NULL THEN
        GRANT EXECUTE ON FUNCTION catalogo.siguiente_referencia_producto(uuid) TO controlplus_app;
    END IF;

    -- 20261106010000_ProductPhysicalDeletionPrivilegesV1
    IF '20261106010000_ProductPhysicalDeletionPrivilegesV1' = ANY (applied) THEN
        GRANT DELETE ON TABLE catalogo.producto_codigo TO controlplus_app;
        GRANT DELETE ON TABLE catalogo.producto TO controlplus_app;
        GRANT DELETE ON TABLE compras.movimiento_inventario TO controlplus_app;
    END IF;

    -- scripts/backups/Initialize-Backups.ps1 (tablas oficiales de respaldo)
    IF to_regclass('configuracion.prueba_restauracion') IS NOT NULL THEN
        GRANT SELECT, INSERT, UPDATE ON
            configuracion.destino_respaldo, configuracion.ejecucion_respaldo,
            configuracion.copia_respaldo, configuracion.prueba_restauracion
        TO controlplus_app;
    END IF;
END
$$;
SELECT 'APP_PRIVILEGES_RESTORED';
