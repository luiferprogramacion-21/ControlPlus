\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- Decisión D3 de la auditoría del 23/11/2026: corregir el medio de pago de una venta de un turno cerrado. Estado antes.
SELECT CASE WHEN count(*) = 38
  AND max("MigrationId") = '20261124010000_AuditDecisionsV1'
  AND count(*) FILTER (WHERE "MigrationId" = '20261124020000_ClosedShiftPaymentCorrectionV1') = 0
  THEN 'history|38|20261124010000_AuditDecisionsV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
-- La función todavía valida solo contra el turno de la venta.
SELECT CASE WHEN pg_get_functiondef('ventas.validar_actor_pago_venta()'::regprocedure) NOT LIKE '%REVERSO_PAGO%'
  AND pg_get_functiondef('ventas.validar_actor_pago_venta()'::regprocedure) LIKE '%actor_valido_turno%'
  THEN 'schema|PENDING' ELSE 'schema|INVALID' END;
-- Huellas que deben quedar idénticas: pagos de venta, movimientos de caja y turnos.
SELECT 'sale_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_venta p;
SELECT 'cash_movements|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(m)::text, '|' ORDER BY m.id), ''))
FROM caja.movimiento_caja m;
SELECT 'shifts|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(t)::text, '|' ORDER BY t.id), ''))
FROM caja.turno_caja t;
SELECT 'CLOSED_SHIFT_PAYMENT_CORRECTION_PREFLIGHT_OK';
ROLLBACK;