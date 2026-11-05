BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL search_path = pg_catalog;
WITH target AS (
  SELECT r.id AS role_id, p.id AS permission_id
  FROM seguridad.rol r CROSS JOIN seguridad.permiso p
  WHERE r.codigo = 'SUPERVISOR' AND p.codigo = 'LAYAWAY.REFUND_DECIDE'
), permission_rows AS (
  SELECT rp.rol_id, rp.permiso_id, rp.fecha_asignacion,
         (rp.rol_id = t.role_id AND rp.permiso_id = t.permission_id) AS is_target,
         encode(sha256(convert_to(to_jsonb(rp)::text,'UTF8')),'hex') AS complete_hash,
         encode(sha256(convert_to((to_jsonb(rp)-'fecha_asignacion')::text,'UTF8')),'hex') AS logical_hash
  FROM seguridad.rol_permiso rp CROSS JOIN target t
), digests AS (
  SELECT count(*) AS total_count,
         count(*) FILTER (WHERE is_target) AS target_count,
         count(*) FILTER (WHERE NOT is_target) AS prior_count,
         bool_and(fecha_asignacion IS NOT NULL) AS timestamps_present,
         encode(sha256(convert_to(coalesce(string_agg(complete_hash,'' ORDER BY complete_hash COLLATE "C")
              FILTER (WHERE NOT is_target),''),'UTF8')),'hex') AS prior_hash,
         encode(sha256(convert_to(coalesce(string_agg(logical_hash,'' ORDER BY logical_hash COLLATE "C"),''),'UTF8')),'hex') AS logical_hash
  FROM permission_rows
)
SELECT json_build_object('database',current_database(),
                         'readOnly',current_setting('transaction_read_only'),
                         'timestampDefault',EXISTS (
                           SELECT 1 FROM pg_attribute a
                           JOIN pg_class c ON c.oid = a.attrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                           WHERE n.nspname = 'seguridad' AND c.relname = 'rol_permiso'
                             AND a.attname = 'fecha_asignacion'
                             AND lower(pg_get_expr(d.adbin,d.adrelid)) IN ('current_timestamp','now()')),
                         'totalCount',total_count,'targetCount',target_count,
                         'priorCount',prior_count,'timestampsPresent',timestamps_present,
                         'priorHash',prior_hash,'logicalHash',logical_hash)::text
FROM digests;
ROLLBACK;
