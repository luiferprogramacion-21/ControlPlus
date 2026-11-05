-- Focused diagnosis only. No credential columns, audit payloads or business values.
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL DateStyle = 'ISO, YMD';
SET LOCAL extra_float_digits = 3;
SET LOCAL search_path = pg_catalog;
SELECT json_build_object('kind','context','key','connection','database',current_database(),
  'server',host(inet_server_addr()),'port',inet_server_port(),'username',current_user,
  'readOnly',current_setting('transaction_read_only'),'isolation',current_setting('transaction_isolation'));
SELECT json_build_object('kind','history','key',"MigrationId")
FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";

-- Bind the focused results to the three recorded table digests, in this transaction.
SELECT format($query$
SELECT json_build_object('kind','table','key',%L,'count',count(*),
 'hash',encode(sha256(convert_to(coalesce(string_agg(h,'' ORDER BY h COLLATE "C"),''),'UTF8')),'hex'))
FROM (SELECT encode(sha256(convert_to(to_jsonb(r)::text,'UTF8')),'hex') h FROM %I.%I r) q;
$query$,n.nspname||'.'||c.relname,n.nspname,c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE (n.nspname,c.relname) IN (('catalogo','producto'),('seguridad','usuario'),('auditoria','evento_auditoria'))
ORDER BY n.nspname,c.relname
\gexec

SELECT json_build_object('kind','product','key',p.id::text,'version',p.version,
 'hash',encode(sha256(convert_to((to_jsonb(p)-'codigo_interno'-'version')::text,'UTF8')),'hex'))
FROM catalogo.producto p WHERE id='01a179ab-2976-74b4-a230-16535087aae1'::uuid;
-- Column names and digests only; product values are not emitted.
SELECT json_build_object('kind','productField','key',e.key,
 'hash',encode(sha256(convert_to(e.value::text,'UTF8')),'hex'))
FROM catalogo.producto p CROSS JOIN LATERAL jsonb_each(to_jsonb(p)) e
WHERE p.id='01a179ab-2976-74b4-a230-16535087aae1'::uuid ORDER BY e.key COLLATE "C";

-- Deliberate allowlist: do not select password_hash, seals, names, email or tokens.
SELECT json_build_object('kind','userState','key',u.id::text,'version',u.version,
 'active',u.activo,'failedAttempts',u.intentos_fallidos,'lockUntil',u.bloqueo_hasta,
 'lastAccess',u.ultimo_acceso,'created',u.fecha_creacion,'modified',u.fecha_modificacion)
FROM seguridad.usuario u ORDER BY u.id;
-- No motivo, datos_anteriores, datos_nuevos, credentials or HTTP bodies.
SELECT json_build_object('kind','audit','key',a.id::text,'utc',a.fecha_hora,
 'action',CASE WHEN a.accion ~ '^[A-Za-z0-9_.:-]{1,100}$' THEN a.accion ELSE '[omitido]' END,
 'entity',CASE WHEN a.entidad ~ '^[A-Za-z0-9_.:-]{1,100}$' THEN a.entidad ELSE '[omitido]' END,
 'entityId',a.entidad_id,
 'result',CASE WHEN a.resultado ~ '^[A-Za-z0-9_.:-]{1,50}$' THEN a.resultado ELSE '[omitido]' END)
FROM auditoria.evento_auditoria a ORDER BY a.id;
ROLLBACK;
