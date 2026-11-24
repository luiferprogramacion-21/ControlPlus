\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 39
  AND max("MigrationId") = '20261124020000_ClosedShiftPaymentCorrectionV1'
  THEN 'history|39|20261124020000_ClosedShiftPaymentCorrectionV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
-- La función admite el pago de una corrección en el turno abierto y conserva la validación del actor.
SELECT CASE WHEN pg_get_functiondef('ventas.validar_actor_pago_venta()'::regprocedure) LIKE '%REVERSO_PAGO%'
  AND pg_get_functiondef('ventas.validar_actor_pago_venta()'::regprocedure) LIKE '%actor_valido_turno%'
  AND EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'tg_pago_venta_validar_actor'
        AND tgrelid = 'ventas.pago_venta'::regclass AND NOT tgisinternal)
  THEN 'schema|OK' ELSE 'schema|INVALID' END;
SELECT 'sale_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_venta p;
SELECT 'cash_movements|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(m)::text, '|' ORDER BY m.id), ''))
FROM caja.movimiento_caja m;
SELECT 'shifts|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(t)::text, '|' ORDER BY t.id), ''))
FROM caja.turno_caja t;
SELECT 'CLOSED_SHIFT_PAYMENT_CORRECTION_POSTFLIGHT_OK';
ROLLBACK;