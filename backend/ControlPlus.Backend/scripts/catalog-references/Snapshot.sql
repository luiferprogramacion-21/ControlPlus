-- Review procedure only. Every statement is read-only; no business values are emitted.
\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL DateStyle = 'ISO, YMD';
SET LOCAL extra_float_digits = 3;
SET LOCAL search_path = pg_catalog;

SELECT json_build_object('kind','database','key','attributes','encoding',pg_encoding_to_char(encoding),
  'provider',datlocprovider::text,'collate',datcollate,'ctype',datctype,'locale',datlocale,
  'icuRules',daticurules,'collationVersion',datcollversion)
FROM pg_database WHERE datname=current_database();

SELECT json_build_object('kind','routine','key',n.nspname||'.'||p.proname||'('||oidvectortypes(p.proargtypes)||')',
  'hash',encode(sha256(convert_to(pg_get_functiondef(p.oid),'UTF8')),'hex'))
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
WHERE p.prokind IN ('f','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
  AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
ORDER BY n.nspname,p.proname,oidvectortypes(p.proargtypes);

SELECT json_build_object('kind','history','key',h."MigrationId",
    'hash',encode(sha256(convert_to(to_jsonb(h)::text,'UTF8')),'hex'))
FROM public."__EFMigrationsHistory" h ORDER BY h."MigrationId";

-- Both counts and sorted SHA-256 row digests are compared, including duplicates.
-- Names come exclusively from pg_catalog and are quoted as identifiers by format.
SELECT format($query$
SELECT json_build_object('kind','table','key',%L,'count',count(*),
  'hash',encode(sha256(convert_to(coalesce(string_agg(h,'' ORDER BY h COLLATE "C"),''),'UTF8')),'hex'))
FROM (SELECT encode(sha256(convert_to(to_jsonb(row_data)::text,'UTF8')),'hex') h
      FROM %I.%I row_data) hashes;
$query$, n.nspname||'.'||c.relname,n.nspname,c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
  AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
ORDER BY n.nspname,c.relname
\gexec

-- Per-table column and relational structure, independent of OIDs/owners.
WITH tables AS (
  SELECT c.oid,n.nspname||'.'||c.relname AS name FROM pg_class c
  JOIN pg_namespace n ON n.oid=c.relnamespace
  WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
    AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
), columns AS (
  SELECT t.name,string_agg(jsonb_build_array(a.attnum,a.attname,format_type(a.atttypid,a.atttypmod),
    a.attnotnull,a.attidentity,a.attgenerated,pg_get_expr(d.adbin,d.adrelid),
    cn.nspname||'.'||co.collname)::text,'' ORDER BY a.attnum) definition
  FROM tables t JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped
  LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
  LEFT JOIN pg_collation co ON co.oid=a.attcollation LEFT JOIN pg_namespace cn ON cn.oid=co.collnamespace
  GROUP BY t.name
), relations AS (
  SELECT t.name,'constraint:'||c.conname AS item,
    jsonb_build_array(pg_get_constraintdef(c.oid,true),c.convalidated,c.condeferrable,c.condeferred)::text AS definition
  FROM tables t JOIN pg_constraint c ON c.conrelid=t.oid
  UNION ALL
  SELECT t.name,'index:'||c.relname,
    jsonb_build_array(pg_get_indexdef(i.indexrelid),i.indisvalid,i.indisready)::text
  FROM tables t JOIN pg_index i ON i.indrelid=t.oid JOIN pg_class c ON c.oid=i.indexrelid
  UNION ALL
  SELECT t.name,'trigger:'||g.tgname,jsonb_build_array(pg_get_triggerdef(g.oid,true),g.tgenabled)::text
  FROM tables t JOIN pg_trigger g ON g.tgrelid=t.oid AND NOT g.tgisinternal
), structures AS (
  SELECT t.name,coalesce(string_agg(jsonb_build_array(r.item,r.definition)::text,'' ORDER BY r.item COLLATE "C"),'') definition
  FROM tables t LEFT JOIN relations r ON r.name=t.name GROUP BY t.name
)
-- Preserve the raw digest; details bind the narrowly approved CHECK equivalence
-- to the complete catalog structure. No relation is omitted from comparison.
SELECT CASE WHEN c.name='compras.movimiento_inventario' THEN
  json_build_object('kind','shape','key',c.name,
    'columnsHash',encode(sha256(convert_to(c.definition,'UTF8')),'hex'),
    'relationsHash',encode(sha256(convert_to(s.definition,'UTF8')),'hex'),
    'relationDetails',(SELECT json_agg(json_build_object('item',r.item,'definition',r.definition)
      ORDER BY r.item COLLATE "C") FROM relations r WHERE r.name=c.name))
ELSE json_build_object('kind','shape','key',c.name,
  'columnsHash',encode(sha256(convert_to(c.definition,'UTF8')),'hex'),
  'relationsHash',encode(sha256(convert_to(s.definition,'UTF8')),'hex')) END
FROM columns c JOIN structures s ON s.name=c.name ORDER BY c.name;

SELECT json_build_object('kind','product','key',p.id::text,'version',p.version,
  'hash',encode(sha256(convert_to((to_jsonb(p)-'codigo_interno'-'version')::text,'UTF8')),'hex'))
FROM catalogo.producto p ORDER BY p.id;

SELECT json_build_object('kind','legacy','key',id::text||':'||code_hash)
FROM (SELECT DISTINCT id,encode(sha256(convert_to(upper(btrim(code)),'UTF8')),'hex') AS code_hash
  FROM (SELECT id,codigo_interno AS code FROM catalogo.producto
        UNION ALL SELECT id,codigo_barras FROM catalogo.producto WHERE codigo_barras IS NOT NULL) codes
  WHERE btrim(code)<>'') keys ORDER BY id,code_hash;

SELECT json_build_object('kind','reference','key',id::text||':'||
  encode(sha256(convert_to(upper(btrim(codigo_interno)),'UTF8')),'hex'))
FROM catalogo.producto ORDER BY id;

-- Conditional SELECT via gexec avoids touching the new table before migration.
SELECT 'SELECT json_build_object(''kind'',''alias'',''key'',producto_id::text||'':''||encode(sha256(convert_to(codigo,''UTF8'')),''hex'')) FROM catalogo.producto_codigo ORDER BY codigo COLLATE "C";'
WHERE to_regclass('catalogo.producto_codigo') IS NOT NULL
\gexec

SELECT format('SELECT json_build_object(''kind'',''sequence'',''key'',%L,''last'',last_value::text,''called'',is_called) FROM %I.%I;',
  n.nspname||'.'||c.relname,n.nspname,c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind='S' AND n.nspname NOT IN ('pg_catalog','information_schema')
  AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
ORDER BY n.nspname,c.relname
\gexec
ROLLBACK;
