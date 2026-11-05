-- Diagnostic SELECTs only: no business rows, provider messages or function bodies.
\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
SET LOCAL DateStyle = 'ISO, YMD';
SET LOCAL extra_float_digits = 3;
SET LOCAL search_path = pg_catalog;
SET LOCAL statement_timeout = '60s';
SET LOCAL lock_timeout = '5s';
SELECT json_build_object('kind','context','key','connection',
 'server',host(inet_server_addr()),'port',inet_server_port(),'database',current_database(),
 'user',current_user,'readOnly',current_setting('transaction_read_only'),
 'isolation',current_setting('transaction_isolation'),'utc',clock_timestamp());
SELECT json_build_object('kind','history','key',"MigrationId")
FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";

-- Each definition is hashed using the exact JSONB encoding used by Snapshot.sql.
-- Only catalog names, technical flags and hashes are emitted, never SQL literals/arguments.
WITH relations AS (
 SELECT 'constraint:'||c.conname AS item,
   jsonb_build_array(pg_get_constraintdef(c.oid,true),c.convalidated,c.condeferrable,c.condeferred)::text AS definition,
   jsonb_build_object('type',c.contype,'validated',c.convalidated,'deferrable',c.condeferrable,
     'deferred',c.condeferred,'columns',c.conkey::text,'referencedColumns',c.confkey::text,
     'referencedSchema',n.nspname,'referencedTable',r.relname,
     'updateAction',c.confupdtype,'deleteAction',c.confdeltype,'matchType',c.confmatchtype,
     'expressionHash',encode(sha256(convert_to(coalesce(pg_get_expr(c.conbin,c.conrelid,true),''),'UTF8')),'hex')) AS properties
 FROM pg_constraint c
 LEFT JOIN pg_class r ON r.oid=c.confrelid LEFT JOIN pg_namespace n ON n.oid=r.relnamespace
 WHERE c.conrelid='compras.movimiento_inventario'::regclass
 UNION ALL
 SELECT 'index:'||c.relname,
   jsonb_build_array(pg_get_indexdef(i.indexrelid),i.indisvalid,i.indisready)::text,
   jsonb_build_object('valid',i.indisvalid,'ready',i.indisready,'unique',i.indisunique,
     'primary',i.indisprimary,'nullsNotDistinct',i.indnullsnotdistinct,
     'keyColumns',i.indnkeyatts,'totalColumns',i.indnatts,'columns',i.indkey::text,
     'predicateHash',encode(sha256(convert_to(coalesce(pg_get_expr(i.indpred,i.indrelid,true),''),'UTF8')),'hex'),
     'expressionHash',encode(sha256(convert_to(coalesce(pg_get_expr(i.indexprs,i.indrelid,true),''),'UTF8')),'hex'))
 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
 WHERE i.indrelid='compras.movimiento_inventario'::regclass
 UNION ALL
 SELECT 'trigger:'||g.tgname,
   jsonb_build_array(pg_get_triggerdef(g.oid,true),g.tgenabled)::text,
   jsonb_build_object('enabled',g.tgenabled,'typeBits',g.tgtype,'columns',g.tgattr::text,
     'deferrable',g.tgdeferrable,'deferred',g.tginitdeferred,
     'functionSchema',n.nspname,'functionName',p.proname,'argumentCount',g.tgnargs,
     'argumentsHash',encode(sha256(g.tgargs),'hex'),'hasWhen',g.tgqual IS NOT NULL)
 FROM pg_trigger g JOIN pg_proc p ON p.oid=g.tgfoid JOIN pg_namespace n ON n.oid=p.pronamespace
 WHERE g.tgrelid='compras.movimiento_inventario'::regclass AND NOT g.tgisinternal
), fingerprint AS (
 SELECT encode(sha256(convert_to(coalesce(string_agg(jsonb_build_array(item,definition)::text,''
   ORDER BY item COLLATE "C"),''),'UTF8')),'hex') AS hash FROM relations
)
SELECT json_build_object('kind','shape','key','compras.movimiento_inventario','relationsHash',hash) FROM fingerprint
UNION ALL
SELECT json_build_object('kind','relation','key',item,
 'definitionHash',encode(sha256(convert_to(definition,'UTF8')),'hex'),'properties',properties)
FROM relations;
ROLLBACK;
