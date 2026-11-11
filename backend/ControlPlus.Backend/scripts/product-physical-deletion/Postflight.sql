BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$
BEGIN
    IF current_setting('transaction_read_only') <> 'on'
       OR current_database() <> 'controlplus'
       OR current_user <> 'postgres'
       OR (SELECT count(*) FROM "__EFMigrationsHistory"
           WHERE "MigrationId" = '20261106010000_ProductPhysicalDeletionPrivilegesV1') <> 1
       OR (SELECT max("MigrationId") FROM "__EFMigrationsHistory") <>
          '20261106010000_ProductPhysicalDeletionPrivilegesV1'
       OR (SELECT count(*) FROM "__EFMigrationsHistory") <> 19
       OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
       OR (EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
           AND (NOT has_table_privilege('controlplus_app', 'catalogo.producto', 'DELETE')
                OR NOT has_table_privilege('controlplus_app', 'catalogo.producto_codigo', 'DELETE')
                OR NOT has_table_privilege('controlplus_app', 'compras.movimiento_inventario', 'DELETE')))
       OR NOT EXISTS (SELECT 1 FROM pg_constraint
                      WHERE conrelid = 'catalogo.producto_codigo'::regclass
                        AND contype = 'f' AND confrelid = 'catalogo.producto'::regclass)
       OR NOT EXISTS (SELECT 1 FROM pg_constraint
                      WHERE conrelid = 'compras.movimiento_inventario'::regclass
                        AND contype = 'f' AND confrelid = 'catalogo.producto'::regclass)
    THEN RAISE EXCEPTION 'Product physical deletion postflight rejected'; END IF;
END $check$;
SELECT 'PRODUCT_PHYSICAL_DELETE_POSTFLIGHT_OK' AS status;
ROLLBACK;
