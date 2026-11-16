-- Comprobación de solo lectura de una base restaurada: historial, establecimiento y que controlplus_app pueda usar
-- todos los objetos de los esquemas de la aplicación (las funciones de trigger no requieren EXECUTE del que inserta). Devuelve una sola fila separada por «|».
SELECT
    (SELECT count(*) FROM public."__EFMigrationsHistory") AS migraciones,
    (SELECT coalesce(max("MigrationId"), '') FROM public."__EFMigrationsHistory") AS ultima_migracion,
    (SELECT count(*) FROM configuracion.establecimiento) AS establecimientos,
    (SELECT count(*) FROM catalogo.producto) AS productos,
    (SELECT count(*) FROM ventas.venta) AS ventas,
    (SELECT coalesce(to_char(max(fecha_hora) AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), '') FROM ventas.venta) AS ultima_venta,
    (SELECT count(*) FROM pg_namespace n
      WHERE n.nspname IN ('auditoria', 'caja', 'catalogo', 'compras', 'configuracion', 'seguridad', 'ventas')
        AND NOT has_schema_privilege('controlplus_app', n.oid, 'USAGE'))
    + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname IN ('auditoria', 'caja', 'catalogo', 'compras', 'configuracion', 'seguridad', 'ventas')
        AND CASE WHEN c.relkind IN ('r', 'p', 'v') THEN NOT has_table_privilege('controlplus_app', c.oid, 'SELECT') ELSE false END)
    + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname IN ('auditoria', 'caja', 'catalogo', 'compras', 'configuracion', 'seguridad', 'ventas')
        -- CASE asegura que has_sequence_privilege solo se evalúe sobre secuencias (el planificador puede
        -- adelantar la función a otros filtros y fallar con «no es una secuencia»).
        AND CASE WHEN c.relkind = 'S' THEN NOT has_sequence_privilege('controlplus_app', c.oid, 'USAGE') ELSE false END)
    + (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
      WHERE n.nspname IN ('auditoria', 'caja', 'catalogo', 'compras', 'configuracion', 'seguridad', 'ventas')
        AND p.prorettype <> 'trigger'::regtype
        AND NOT has_function_privilege('controlplus_app', p.oid, 'EXECUTE')) AS objetos_sin_acceso;
