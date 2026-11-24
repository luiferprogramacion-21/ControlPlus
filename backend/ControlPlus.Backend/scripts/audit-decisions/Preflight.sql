\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- Decisiones de la auditoría del 23/11/2026 (D1, D7 y D8): estado antes de activar.
SELECT CASE WHEN count(*) = 37
  AND max("MigrationId") = '20261121020000_CreditDefaultMonthsV1'
  AND count(*) FILTER (WHERE "MigrationId" LIKE '20261124%') = 0
  THEN 'history|37|20261121020000_CreditDefaultMonthsV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
-- Plazos aún de 90 días y 3 meses, y las dos funciones aún exigen la referencia.
SELECT CASE WHEN pg_get_constraintdef((SELECT oid FROM pg_constraint
        WHERE conrelid = 'configuracion.establecimiento'::regclass
          AND conname = 'ck_establecimiento_dias_credito_predeterminado')) LIKE '%90%'
  AND pg_get_constraintdef((SELECT oid FROM pg_constraint
        WHERE conrelid = 'configuracion.establecimiento'::regclass
          AND conname = 'ck_establecimiento_meses_credito_predeterminado')) LIKE '%3%'
  AND pg_get_functiondef('ventas.validar_referencia_reembolso()'::regprocedure) LIKE '%requiere_referencia%'
  AND pg_get_functiondef('ventas.validar_datos_pago_cambio()'::regprocedure) LIKE '%requiere_referencia%'
  THEN 'schema|PENDING' ELSE 'schema|INVALID' END;
-- Huellas que deben quedar idénticas: establecimiento, devoluciones de apartados, pagos de cambios y los límites de
-- descuento de los roles distintos de Administrador.
SELECT 'establishment|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(e)::text, '|' ORDER BY e.id), ''))
FROM configuracion.establecimiento e;
SELECT 'layaway_refunds|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(r)::text, '|' ORDER BY r.id), ''))
FROM ventas.reembolso_apartado r;
SELECT 'change_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_cambio_venta p;
SELECT 'other_limits|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(l)::text, '|' ORDER BY l.id), ''))
FROM seguridad.limite_operacion_rol l JOIN seguridad.rol r ON r.id = l.rol_id
WHERE NOT (r.codigo = 'ADMINISTRADOR' AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE');
SELECT 'AUDIT_DECISIONS_PREFLIGHT_OK';
ROLLBACK;
