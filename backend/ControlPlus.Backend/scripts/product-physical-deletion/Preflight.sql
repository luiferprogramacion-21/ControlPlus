BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$
BEGIN
    IF current_setting('transaction_read_only') <> 'on'
       OR current_database() <> 'controlplus'
       OR current_user <> 'postgres'
       OR EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                  WHERE "MigrationId" = '20261106010000_ProductPhysicalDeletionPrivilegesV1')
       OR to_regclass('catalogo.producto') IS NULL
       OR to_regclass('catalogo.producto_codigo') IS NULL
       OR to_regclass('compras.movimiento_inventario') IS NULL
       OR NOT EXISTS (SELECT 1 FROM pg_constraint
                      WHERE conrelid = 'catalogo.producto_codigo'::regclass
                        AND contype = 'f' AND confrelid = 'catalogo.producto'::regclass)
       OR NOT EXISTS (SELECT 1 FROM pg_constraint
                      WHERE conrelid = 'compras.movimiento_inventario'::regclass
                        AND contype = 'f' AND confrelid = 'catalogo.producto'::regclass)
       OR NOT EXISTS (SELECT 1 FROM pg_trigger
                      WHERE tgrelid = 'compras.movimiento_inventario'::regclass
                        AND tgname = 'trg_movimiento_inventario_proteger_orden_registro'
                        AND NOT tgisinternal)
       OR EXISTS (SELECT 1 FROM pg_trigger
                  WHERE tgrelid = 'compras.movimiento_inventario'::regclass
                    AND (tgtype::int & 8) <> 0 AND NOT tgisinternal)
       OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                      WHERE "MigrationId" = '20261105010000_SupervisorLayawayRefundDecisionV1')
       OR (SELECT max("MigrationId") FROM "__EFMigrationsHistory") <>
          '20261105010000_SupervisorLayawayRefundDecisionV1'
       OR (SELECT count(*) FROM "__EFMigrationsHistory") <> 18
       OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
    THEN RAISE EXCEPTION 'Product physical deletion preflight rejected'; END IF;
END $check$;
SELECT 'PRODUCT_PHYSICAL_DELETE_PREFLIGHT_OK' AS status;
ROLLBACK;
