\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- Referencia opcional en abonos de crédito y pagos de apartado (21 de noviembre de 2026): estado antes de activar.
SELECT CASE WHEN count(*) = 35
  AND max("MigrationId") = '20261120050000_AdditionalSaleLinkV1'
  AND count(*) FILTER (WHERE "MigrationId" LIKE '20261121%') = 0
  THEN 'history|35|20261120050000_AdditionalSaleLinkV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
-- La función compartida todavía exige la referencia y la usan solo los triggers de abonos y pagos de apartado.
SELECT CASE WHEN pg_get_functiondef('ventas.validar_datos_cobro()'::regprocedure) LIKE '%requiere_referencia%'
  AND (SELECT count(*) FROM pg_trigger t WHERE t.tgfoid = 'ventas.validar_datos_cobro()'::regprocedure AND NOT t.tgisinternal) = 2
  AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'ventas.pago_credito'::regclass
        AND tgname = 'tg_pago_credito_validar_datos' AND tgfoid = 'ventas.validar_datos_cobro()'::regprocedure)
  AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'ventas.pago_apartado'::regclass
        AND tgname = 'tg_pago_apartado_validar_datos' AND tgfoid = 'ventas.validar_datos_cobro()'::regprocedure)
  THEN 'function|PENDING' ELSE 'function|INVALID' END;
-- Huellas de los pagos existentes: después de activar deben ser idénticas.
SELECT 'credit_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_credito p;
SELECT 'layaway_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_apartado p;
SELECT 'OPTIONAL_PAYMENT_REFERENCE_PREFLIGHT_OK';
ROLLBACK;
