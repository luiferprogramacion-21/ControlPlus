\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 30
  AND max("MigrationId") = '20261119020000_OptionalEstablishmentIdentificationV1'
  THEN 'history|30|20261119020000_OptionalEstablishmentIdentificationV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT CASE WHEN (SELECT is_nullable FROM information_schema.columns
  WHERE table_schema = 'configuracion' AND table_name = 'establecimiento'
    AND column_name = 'identificacion') = 'YES'
  THEN 'schema|OPTIONAL' ELSE 'schema|INVALID' END;
-- Los datos no cambian: la huella debe ser la misma del preflight.
SELECT 'establishments|' || count(*) || '|' ||
  md5(coalesce(string_agg(to_jsonb(e)::text, '|' ORDER BY e.id), ''))
FROM configuracion.establecimiento e;
SELECT 'OPTIONAL_NIT_POSTFLIGHT_OK';
ROLLBACK;
