\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 35
  AND max("MigrationId") = '20261120050000_AdditionalSaleLinkV1'
  AND count(*) FILTER (WHERE "MigrationId" LIKE '20261120%') = 5
  THEN 'history|35|20261120050000_AdditionalSaleLinkV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
SELECT CASE WHEN to_regclass('ventas.correccion_apartado') IS NOT NULL
  AND to_regclass('ventas.reembolso_correccion_apartado') IS NOT NULL
  AND to_regclass('caja.revision_cierre') IS NOT NULL
  AND to_regclass('compras.correccion_movimiento_inventario') IS NOT NULL
  AND to_regclass('compras.correccion_recepcion') IS NOT NULL
  AND (SELECT count(*) FROM information_schema.columns WHERE
        (table_schema, table_name, column_name) IN (('ventas','venta','venta_origen_id'),
          ('ventas','apartado','total_original'), ('ventas','detalle_apartado','vigente'))) = 3
  THEN 'schema|OK' ELSE 'schema|INVALID' END;
-- Lo existente queda como estaba: apartados con su total original, líneas vigentes, ventas sin origen y tablas nuevas vacías.
SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM ventas.apartado WHERE total_original IS DISTINCT FROM total)
  AND NOT EXISTS (SELECT 1 FROM ventas.detalle_apartado WHERE NOT vigente)
  AND NOT EXISTS (SELECT 1 FROM ventas.venta WHERE venta_origen_id IS NOT NULL)
  AND (SELECT count(*) FROM ventas.correccion_apartado) = 0
  AND (SELECT count(*) FROM ventas.reembolso_correccion_apartado) = 0
  AND (SELECT count(*) FROM caja.revision_cierre) = 0
  AND (SELECT count(*) FROM compras.correccion_movimiento_inventario) = 0
  AND (SELECT count(*) FROM compras.correccion_recepcion) = 0
  THEN 'backfill|OK' ELSE 'backfill|INVALID' END;
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
-- Regla del proyecto: toda llave foránea nueva es ON DELETE RESTRICT.
SELECT CASE WHEN NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE contype = 'f' AND confdeltype <> 'r'
      AND conrelid IN ('ventas.correccion_apartado'::regclass, 'ventas.reembolso_correccion_apartado'::regclass,
                       'caja.revision_cierre'::regclass, 'compras.correccion_movimiento_inventario'::regclass,
                       'compras.correccion_recepcion'::regclass, 'ventas.venta'::regclass))
  THEN 'fks|RESTRICT' ELSE 'fks|INVALID' END;
SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
  AND has_table_privilege('controlplus_app', 'ventas.correccion_apartado', 'INSERT')
  AND has_table_privilege('controlplus_app', 'ventas.reembolso_correccion_apartado', 'INSERT')
  AND has_table_privilege('controlplus_app', 'caja.revision_cierre', 'INSERT')
  AND has_table_privilege('controlplus_app', 'compras.correccion_movimiento_inventario', 'INSERT')
  AND has_table_privilege('controlplus_app', 'compras.correccion_recepcion', 'INSERT')
  AND has_table_privilege('controlplus_app', 'ventas.detalle_apartado', 'UPDATE')
  THEN 'privileges|OK' ELSE 'privileges|INVALID' END;
SELECT 'OPERATIONAL_CORRECTIONS_POSTFLIGHT_OK';
ROLLBACK;
