\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 26 AND max("MigrationId") = '20261116010000_CreditRiskControlsV1'
  AND count(*) FILTER (WHERE "MigrationId" = '20261116020000_CreditInstallmentsV1') = 0
  THEN 'history|26|20261116010000_CreditRiskControlsV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
SELECT 'credits|' || count(*) || '|' || coalesce(sum(monto_ajustado),0) || '|' || coalesce(sum(saldo_pendiente),0)
FROM ventas.credito;
SELECT 'payments|' || count(*) || '|' || coalesce(sum(valor_aplicado),0)
FROM ventas.pago_credito WHERE estado = 'APLICADO';
SELECT 'CREDIT_INSTALLMENTS_PREFLIGHT_OK';
ROLLBACK;
