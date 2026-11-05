\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_setting('transaction_read_only') = 'on'
   AND current_database() = 'controlplus'
   AND current_user = 'postgres'
   AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999
   AND (SELECT count(*) FROM public."__EFMigrationsHistory") = 15
   AND (SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1)
       = '20261102020000_CategoryProductReferencesV1'
   AND (SELECT count(*) FROM catalogo.categoria WHERE abreviatura IS NOT NULL) = 0
   AND to_regprocedure('catalogo.siguiente_referencia_producto(uuid)') IS NOT NULL
   AND to_regprocedure('catalogo.siguiente_referencia_producto()') IS NOT NULL
   AND EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conrelid = 'catalogo.categoria'::regclass
       AND c.conname = 'ck_categoria_abreviatura' AND c.contype = 'c' AND c.convalidated)
   AND EXISTS (SELECT 1 FROM pg_index i JOIN pg_class x ON x.oid = i.indexrelid
       WHERE i.indrelid = 'catalogo.categoria'::regclass AND x.relname = 'uq_categoria_abreviatura'
         AND i.indisunique AND i.indisvalid AND i.indisready);
ROLLBACK;
