\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- Resumable (20 November 2026): the first attempt on the store applied SaleCorrectionsV1 (history 31) and stopped at the
-- second migration. Both starting points are accepted; nothing else of the activation exists in either.
SELECT CASE
  WHEN count(*) = 30 AND max("MigrationId") = '20261119020000_OptionalEstablishmentIdentificationV1'
    AND count(*) FILTER (WHERE "MigrationId" LIKE '20261120%') = 0
  THEN 'history|30|20261119020000_OptionalEstablishmentIdentificationV1'
  WHEN count(*) = 31 AND max("MigrationId") = '20261120010000_SaleCorrectionsV1'
    AND count(*) FILTER (WHERE "MigrationId" LIKE '20261120%') = 1
  THEN 'history|31|20261120010000_SaleCorrectionsV1'
  ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT 'clients|' || count(*) FROM pg_stat_activity
WHERE datname = current_database() AND pid <> pg_backend_pid();
SELECT CASE WHEN to_regclass('ventas.correccion_apartado') IS NULL
  AND to_regclass('ventas.reembolso_correccion_apartado') IS NULL
  AND to_regclass('caja.revision_cierre') IS NULL
  AND to_regclass('compras.correccion_movimiento_inventario') IS NULL
  AND to_regclass('compras.correccion_recepcion') IS NULL
  AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE
        (table_schema, table_name, column_name) IN (('ventas','venta','venta_origen_id'),
          ('ventas','apartado','total_original'), ('ventas','detalle_apartado','vigente')))
  THEN 'schema|PENDING' ELSE 'schema|INVALID' END;
-- Huellas de los datos que existen hoy: después de activar deben ser idénticas (sin contar las columnas nuevas).
SELECT 'sales|' || count(*) || '|' || md5(coalesce(string_agg((to_jsonb(v) - 'venta_origen_id')::text, '|' ORDER BY v.id), ''))
FROM ventas.venta v;
SELECT 'payments|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(p)::text, '|' ORDER BY p.id), ''))
FROM ventas.pago_venta p;
SELECT 'layaways|' || count(*) || '|' || md5(coalesce(string_agg((to_jsonb(a) - 'total_original')::text, '|' ORDER BY a.id), ''))
FROM ventas.apartado a;
SELECT 'layaway_lines|' || count(*) || '|' || md5(coalesce(string_agg((to_jsonb(d) - 'vigente')::text, '|' ORDER BY d.id), ''))
FROM ventas.detalle_apartado d;
SELECT 'cash|' || count(*) || '|' || md5(coalesce(string_agg(to_jsonb(m)::text, '|' ORDER BY m.id), ''))
FROM caja.movimiento_caja m;
SELECT 'OPERATIONAL_CORRECTIONS_PREFLIGHT_OK';
ROLLBACK;
