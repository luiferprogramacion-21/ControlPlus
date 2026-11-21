\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 37
  AND max("MigrationId") = '20261121020000_CreditDefaultMonthsV1'
  THEN 'history|37|20261121020000_CreditDefaultMonthsV1'
  ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'configuracion' AND table_name = 'establecimiento'
      AND column_name = 'meses_credito_predeterminado' AND is_nullable = 'YES')
  AND EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid = 'configuracion.establecimiento'::regclass
      AND conname = 'ck_establecimiento_meses_credito_predeterminado' AND convalidated
      AND pg_get_constraintdef(oid) LIKE '%meses_credito_predeterminado%')
  AND EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid = 'configuracion.establecimiento'::regclass
      AND conname = 'ck_establecimiento_plazo_credito_exclusivo' AND convalidated
      AND pg_get_constraintdef(oid) LIKE '%dias_credito_predeterminado%'
      AND pg_get_constraintdef(oid) LIKE '%meses_credito_predeterminado%')
  AND NOT EXISTS (
    SELECT 1 FROM configuracion.establecimiento
    WHERE meses_credito_predeterminado IS NOT NULL)
  THEN 'schema|OK' ELSE 'schema|INVALID' END;
-- La columna nueva se omite para cotejar exactamente los datos preexistentes.
SELECT 'establishment|' || count(*) || '|' ||
  md5(coalesce(string_agg((to_jsonb(e) - 'meses_credito_predeterminado')::text, '|' ORDER BY e.id), ''))
FROM configuracion.establecimiento e;
SELECT 'CREDIT_DEFAULT_MONTHS_POSTFLIGHT_OK';
ROLLBACK;
