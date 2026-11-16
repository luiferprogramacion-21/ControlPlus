\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 27 AND max("MigrationId") = '20261116020000_CreditInstallmentsV1'
  THEN 'history|27|20261116020000_CreditInstallmentsV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'credits|' || count(*) || '|' || coalesce(sum(monto_ajustado),0) || '|' || coalesce(sum(saldo_pendiente),0)
FROM ventas.credito;
SELECT 'payments|' || count(*) || '|' || coalesce(sum(valor_aplicado),0)
FROM ventas.pago_credito WHERE estado = 'APLICADO';
SELECT CASE WHEN (SELECT count(*) FROM ventas.cuota_credito) = (SELECT count(*) FROM ventas.credito)
  AND NOT EXISTS (SELECT 1 FROM ventas.credito c LEFT JOIN ventas.cuota_credito q ON q.credito_id=c.id
                  GROUP BY c.id,c.monto_ajustado,c.saldo_pendiente
                  HAVING count(q.id)<>1 OR coalesce(sum(q.monto),0)<>c.monto_ajustado OR coalesce(sum(q.saldo_pendiente),0)<>c.saldo_pendiente)
  THEN 'installments|OK' ELSE 'installments|INVALID' END;
SELECT CASE WHEN NOT EXISTS (
  SELECT 1 FROM ventas.pago_credito p LEFT JOIN ventas.aplicacion_pago_cuota a ON a.pago_credito_id=p.id
  WHERE p.estado='APLICADO' GROUP BY p.id,p.valor_aplicado
  HAVING coalesce(sum(a.valor_aplicado),0)<>p.valor_aplicado)
  THEN 'applications|OK' ELSE 'applications|INVALID' END;
SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app') OR
  (has_table_privilege('controlplus_app','ventas.cuota_credito','SELECT,INSERT,UPDATE,DELETE') AND
   has_table_privilege('controlplus_app','ventas.aplicacion_pago_cuota','SELECT,INSERT,UPDATE,DELETE'))
  THEN 'privileges|OK' ELSE 'privileges|INVALID' END;
SELECT 'CREDIT_INSTALLMENTS_POSTFLIGHT_OK';
ROLLBACK;
