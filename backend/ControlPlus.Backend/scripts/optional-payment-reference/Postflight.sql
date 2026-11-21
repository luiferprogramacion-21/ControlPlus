\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 36
  AND max("MigrationId") = '20261121010000_OptionalCreditLayawayPaymentReferenceV1'
  THEN 'history|36|20261121010000_OptionalCreditLayawayPaymentReferenceV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
-- La función ya no exige la referencia, conserva el rechazo de una referencia vacía y los triggers no cambiaron.
SELECT CASE WHEN pg_get_functiondef('ventas.validar_datos_cobro()'::regprocedure) NOT LIKE '%requiere_referencia%'
  AND pg_get_functiondef('ventas.validar_datos_cobro()'::regprocedure) LIKE '%La referencia opcional no puede estar vacia.%'
  AND pg_get_functiondef('ventas.validar_datos_cobro()'::regprocedure) LIKE '%requiere registrar el valor recibido.%'
  AND (SELECT count(*) FROM pg_trigger t WHERE t.tgfoid = 'ventas.validar_datos_cobro()'::regprocedure AND NOT t.tgisinternal) = 2
  AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'ventas.pago_credito'::regclass
        AND tgname = 'tg_pago_credito_validar_datos' AND tgfoid = 'ventas.validar_datos_cobro()'::regprocedure)
  AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'ventas.pago_apartado'::regclass
        AND tgname = 'tg_pago_apartado_validar_datos' AND tgfoid = 'ventas.validar_datos_cobro()'::regprocedure)
  THEN 'function|OK' ELSE 'function|INVALID' END;
SELECT 'credit_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_credito p;
SELECT 'layaway_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_apartado p;
SELECT 'OPTIONAL_PAYMENT_REFERENCE_POSTFLIGHT_OK';
ROLLBACK;
