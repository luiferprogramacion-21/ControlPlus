\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- La versión anterior debe estar completa y el nuevo esquema pendiente.
SELECT CASE WHEN count(*) = 36
  AND max("MigrationId") = '20261121010000_OptionalCreditLayawayPaymentReferenceV1'
  THEN 'history|36|20261121010000_OptionalCreditLayawayPaymentReferenceV1'
  ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'configuracion' AND table_name = 'establecimiento'
      AND column_name = 'meses_credito_predeterminado')
  AND EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid = 'configuracion.establecimiento'::regclass
      AND conname = 'ck_establecimiento_dias_credito_predeterminado' AND convalidated)
  AND NOT EXISTS (
    SELECT 1 FROM configuracion.establecimiento
    WHERE dias_credito_predeterminado IS NOT NULL
      AND dias_credito_predeterminado NOT BETWEEN 1 AND 90)
  THEN 'schema|PENDING' ELSE 'schema|INVALID' END;
SELECT 'establishment|' || count(*) || '|' ||
  md5(coalesce(string_agg(to_jsonb(e)::text, '|' ORDER BY e.id), ''))
FROM configuracion.establecimiento e;
SELECT 'CREDIT_DEFAULT_MONTHS_PREFLIGHT_OK';
ROLLBACK;
