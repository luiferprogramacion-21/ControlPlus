\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 29
  AND max("MigrationId") = '20261119013000_HeldSalesV1'
  AND count(*) FILTER (WHERE "MigrationId" = '20261119020000_OptionalEstablishmentIdentificationV1') = 0
  THEN 'history|29|20261119013000_HeldSalesV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
SELECT CASE WHEN (SELECT is_nullable FROM information_schema.columns
  WHERE table_schema = 'configuracion' AND table_name = 'establecimiento'
    AND column_name = 'identificacion') = 'NO'
  THEN 'schema|PENDING' ELSE 'schema|INVALID' END;
SELECT 'establishments|' || count(*) || '|' ||
  md5(coalesce(string_agg(to_jsonb(e)::text, '|' ORDER BY e.id), ''))
FROM configuracion.establecimiento e;
SELECT 'OPTIONAL_NIT_PREFLIGHT_OK';
ROLLBACK;
