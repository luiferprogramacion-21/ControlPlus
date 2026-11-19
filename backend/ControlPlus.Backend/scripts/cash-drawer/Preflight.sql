\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 27
  AND max("MigrationId") = '20261116020000_CreditInstallmentsV1'
  AND count(*) FILTER (WHERE "MigrationId" = '20261119010000_CashDrawerV1') = 0
  THEN 'history|27|20261116020000_CreditInstallmentsV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
SELECT CASE WHEN to_regclass('caja.apertura_cajon') IS NULL
  AND NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'configuracion' AND table_name = 'perfil_impresora'
      AND column_name IN ('cajon_habilitado','cajon_pin','cajon_pulso_on_ms','cajon_pulso_off_ms'))
  THEN 'schema|PENDING' ELSE 'schema|INVALID' END;
SELECT 'profiles|' || count(*) || '|' ||
  md5(coalesce(string_agg((to_jsonb(p) - 'cajon_habilitado' - 'cajon_pin'
    - 'cajon_pulso_on_ms' - 'cajon_pulso_off_ms')::text, '|' ORDER BY p.id), ''))
FROM configuracion.perfil_impresora p;
SELECT 'jobs|' || count(*) || '|' ||
  md5(coalesce(string_agg(to_jsonb(j)::text, '|' ORDER BY j.id), ''))
FROM configuracion.trabajo_impresion j;
SELECT 'CASH_DRAWER_PREFLIGHT_OK';
ROLLBACK;
