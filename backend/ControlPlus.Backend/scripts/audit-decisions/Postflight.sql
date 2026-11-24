\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 38
  AND max("MigrationId") = '20261124010000_AuditDecisionsV1'
  THEN 'history|38|20261124010000_AuditDecisionsV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
-- Plazos de 180 días y 6 meses; las funciones ya no exigen la referencia pero rechazan una en blanco; Administrador al 100 %.
SELECT CASE WHEN pg_get_constraintdef((SELECT oid FROM pg_constraint
        WHERE conrelid = 'configuracion.establecimiento'::regclass
          AND conname = 'ck_establecimiento_dias_credito_predeterminado')) LIKE '%180%'
  AND pg_get_constraintdef((SELECT oid FROM pg_constraint
        WHERE conrelid = 'configuracion.establecimiento'::regclass
          AND conname = 'ck_establecimiento_meses_credito_predeterminado')) LIKE '%6%'
  AND pg_get_functiondef('ventas.validar_referencia_reembolso()'::regprocedure) NOT LIKE '%requiere_referencia%'
  AND pg_get_functiondef('ventas.validar_referencia_reembolso()'::regprocedure) LIKE '%opcional no puede estar vacia%'
  AND pg_get_functiondef('ventas.validar_datos_pago_cambio()'::regprocedure) NOT LIKE '%requiere_referencia%'
  AND pg_get_functiondef('ventas.validar_datos_pago_cambio()'::regprocedure) LIKE '%opcional no puede estar vacia%'
  AND NOT EXISTS (SELECT 1 FROM seguridad.limite_operacion_rol l JOIN seguridad.rol r ON r.id = l.rol_id
        WHERE r.codigo = 'ADMINISTRADOR' AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE' AND l.valor_maximo <> 100)
  THEN 'schema|OK' ELSE 'schema|INVALID' END;
SELECT 'establishment|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(e)::text, '|' ORDER BY e.id), ''))
FROM configuracion.establecimiento e;
SELECT 'layaway_refunds|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(r)::text, '|' ORDER BY r.id), ''))
FROM ventas.reembolso_apartado r;
SELECT 'change_payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_cambio_venta p;
SELECT 'other_limits|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(l)::text, '|' ORDER BY l.id), ''))
FROM seguridad.limite_operacion_rol l JOIN seguridad.rol r ON r.id = l.rol_id
WHERE NOT (r.codigo = 'ADMINISTRADOR' AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE');
SELECT 'AUDIT_DECISIONS_POSTFLIGHT_OK';
ROLLBACK;
