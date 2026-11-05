-- Evidence only. No DDL, DML, temporary objects, sequence consumption or API calls.
\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL statement_timeout = '60s';
SET LOCAL lock_timeout = '5s';
SET LOCAL search_path = pg_catalog;
SELECT json_build_object('kind','target','server',host(inet_server_addr()),'port',inet_server_port(),
  'username',current_user,'sessionUser',session_user,'database',current_database(),
  'readOnly',current_setting('transaction_read_only'),
  'isolation',current_setting('transaction_isolation'),'serverVersion',current_setting('server_version_num')::int);

WITH visibility AS (
  SELECT coalesce((SELECT rolsuper FROM pg_roles WHERE rolname=current_user),false)
    OR pg_has_role(current_user,'pg_read_all_stats','USAGE') AS full_stats
), sessions AS (
  SELECT count(*) FILTER (WHERE backend_type='client backend') AS clients,
    count(*) FILTER (WHERE backend_type IS NULL) AS unclassified
  FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()
)
SELECT json_build_object('kind','connections','fullVisibility',v.full_stats,
  'visibleOtherClients',s.clients,'unclassifiedOtherSessions',s.unclassified)
FROM visibility v CROSS JOIN sessions s;

SELECT coalesce(EXISTS(SELECT 1 FROM pg_attribute
  WHERE attrelid=to_regclass('public."__EFMigrationsHistory"')
    AND attname='MigrationId' AND NOT attisdropped),false) AS history_readable
\gset
\if :history_readable
SELECT json_build_object('kind','history','readable',true,
  'items',coalesce(json_agg("MigrationId" ORDER BY "MigrationId"),'[]'::json))
FROM public."__EFMigrationsHistory";
\else
SELECT json_build_object('kind','history','readable',false,'items','[]'::json);
\endif

WITH expected(name,type_name,not_null) AS (VALUES
  ('id','uuid',true),('categoria_id','uuid',true),('unidad_medida_id','uuid',true),('proveedor_id','uuid',false),
  ('codigo_interno','character varying(50)',true),('codigo_barras','character varying(100)',false),
  ('formato_codigo_barras','character varying(20)',false),('codigo_barras_generado','boolean',true),
  ('nombre','character varying(200)',true),('descripcion','text',false),('precio_minorista','numeric(18,0)',true),
  ('precio_mayorista','numeric(18,0)',false),('costo_promedio','numeric(18,4)',false),('porcentaje_iva','numeric(5,2)',false),
  ('stock_actual','integer',true),('stock_reservado','integer',true),('stock_minimo','integer',true),('activo','boolean',true),
  ('usuario_creacion_id','uuid',true),('usuario_modificacion_id','uuid',false),
  ('fecha_creacion','timestamp with time zone',true),('fecha_modificacion','timestamp with time zone',false),('version','bigint',true)
), bad AS (
  SELECT e.name FROM expected e LEFT JOIN pg_attribute a
    ON a.attrelid=to_regclass('catalogo.producto') AND a.attname=e.name AND a.attnum>0 AND NOT a.attisdropped
  WHERE a.attname IS NULL OR format_type(a.atttypid,a.atttypmod)<>e.type_name OR a.attnotnull<>e.not_null
)
SELECT json_build_object('kind','columns','productExists',to_regclass('catalogo.producto') IS NOT NULL,
  'mismatches',coalesce(json_agg(name ORDER BY name),'[]'::json)) FROM bad;

WITH expected(name,kind,column_name) AS (VALUES
  ('pk_producto','p','id'),('uq_producto_codigo_interno','u','codigo_interno'),
  ('uq_producto_codigo_barras','u','codigo_barras')
), bad AS (
  SELECT e.name FROM expected e WHERE NOT EXISTS (
    SELECT 1 FROM pg_constraint c JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attname=e.column_name
      JOIN pg_index i ON i.indexrelid=c.conindid
    WHERE c.conrelid=to_regclass('catalogo.producto') AND c.conname=e.name AND c.contype::text=e.kind
      AND c.conkey=ARRAY[a.attnum] AND c.convalidated AND i.indisvalid AND i.indisready AND NOT i.indnullsnotdistinct)
)
SELECT json_build_object('kind','constraints','keyMismatches',coalesce(json_agg(name ORDER BY name),'[]'::json),
  'formatValidated',EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid=to_regclass('catalogo.producto')
      AND conname='ck_producto_formato_codigo' AND contype='c' AND convalidated),
  'formatDefinition',(SELECT pg_get_constraintdef(oid,true) FROM pg_constraint
    WHERE conrelid=to_regclass('catalogo.producto') AND conname='ck_producto_formato_codigo'),
  'nonBlankCodeChecksValidated',(SELECT count(*)=2 FROM pg_constraint WHERE conrelid=to_regclass('catalogo.producto')
      AND conname IN ('ck_producto_codigo','ck_producto_codigo_barras') AND contype='c' AND convalidated)) FROM bad;

WITH objects(name,present) AS (VALUES
  ('catalogo.producto_codigo',to_regclass('catalogo.producto_codigo') IS NOT NULL),
  ('catalogo.producto_referencia_seq',to_regclass('catalogo.producto_referencia_seq') IS NOT NULL),
  ('catalogo.siguiente_referencia_producto()',to_regprocedure('catalogo.siguiente_referencia_producto()') IS NOT NULL),
  ('catalogo.reservar_codigo_producto(text,uuid)',to_regprocedure('catalogo.reservar_codigo_producto(text,uuid)') IS NOT NULL),
  ('catalogo.proteger_codigos_producto()',to_regprocedure('catalogo.proteger_codigos_producto()') IS NOT NULL),
  ('catalogo.registrar_codigos_producto()',to_regprocedure('catalogo.registrar_codigos_producto()') IS NOT NULL)
)
SELECT json_build_object('kind','reserved','present',coalesce(json_agg(name ORDER BY name) FILTER(WHERE present),'[]'::json)) FROM objects;
SELECT json_build_object('kind','sideEffects',
  'enabledProductTriggers',(SELECT count(*) FROM pg_trigger WHERE tgrelid=to_regclass('catalogo.producto')
    AND NOT tgisinternal AND tgenabled<>'D'),
  'productRules',(SELECT count(*) FROM pg_rewrite WHERE ev_class=to_regclass('catalogo.producto') AND rulename<>'_RETURN'));

WITH expected(name,type_name) AS (VALUES ('id','uuid'),('codigo_interno','character varying(50)'),
 ('codigo_barras','character varying(100)'),('codigo_barras_generado','boolean'),
 ('formato_codigo_barras','character varying(20)'),('fecha_creacion','timestamp with time zone'),('version','bigint'))
SELECT (SELECT count(*)=7 FROM expected e JOIN pg_attribute a ON a.attrelid=to_regclass('catalogo.producto')
  AND a.attname=e.name AND NOT a.attisdropped AND format_type(a.atttypid,a.atttypmod)=e.type_name) AS product_readable
\gset
\if :product_readable
SELECT json_build_object('kind','data','readable',true,'ambiguousCodeGroups',(
  SELECT count(*) FROM (SELECT upper(btrim(code)) FROM (
    SELECT id,codigo_interno AS code FROM catalogo.producto
    UNION ALL SELECT id,codigo_barras FROM catalogo.producto WHERE codigo_barras IS NOT NULL
  ) codes GROUP BY upper(btrim(code)) HAVING count(DISTINCT id)>1) conflicts),
  'versionsAtMaximum',(SELECT count(*) FROM catalogo.producto WHERE version=9223372036854775807));
\else
SELECT json_build_object('kind','data','readable',false,'ambiguousCodeGroups',NULL,'versionsAtMaximum',NULL);
\endif
ROLLBACK;
