\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_setting('transaction_read_only') = 'on'
   AND current_database() ~ '^(controlplus|controlplus_test_[a-f0-9]{32})$'
   AND (current_database() = 'controlplus' OR
       (current_user = 'controlplus_test_admin' AND
        (SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = current_database()) = 'controlplus_test_admin'))
   AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999
   AND NOT EXISTS (SELECT 1 FROM pg_stat_activity
       WHERE datname = current_database() AND pid <> pg_backend_pid())
   AND (SELECT count(*) FROM public."__EFMigrationsHistory") = 14
   AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1)
       = '20261102010000_GrantProductCodeAccessV1'
   AND NOT EXISTS (SELECT 1 FROM public."__EFMigrationsHistory"
       WHERE "MigrationId" = '20261102020000_CategoryProductReferencesV1')
   AND NOT EXISTS (SELECT 1 FROM information_schema.columns
       WHERE table_schema = 'catalogo' AND table_name = 'categoria' AND column_name = 'abreviatura')
   AND to_regprocedure('catalogo.siguiente_referencia_producto(uuid)') IS NULL
   AND to_regprocedure('catalogo.siguiente_referencia_producto()') IS NOT NULL
   AND to_regclass('catalogo.producto_referencia_seq') IS NOT NULL
   AND to_regclass('catalogo.producto_codigo') IS NOT NULL;
ROLLBACK;
