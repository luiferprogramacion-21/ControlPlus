BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_database() AS database_name,
       current_user AS inspector_role,
       EXISTS (
           SELECT 1 FROM "__EFMigrationsHistory"
           WHERE "MigrationId" = '20261106010000_ProductPhysicalDeletionPrivilegesV1'
       ) AS migration_applied,
       has_table_privilege('controlplus_app', 'catalogo.producto', 'DELETE') AS product_delete_granted,
       has_table_privilege('controlplus_app', 'catalogo.producto_codigo', 'DELETE') AS alias_delete_granted,
       has_table_privilege('controlplus_app', 'compras.movimiento_inventario', 'DELETE') AS movement_delete_granted;
ROLLBACK;
