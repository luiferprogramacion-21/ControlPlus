\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL search_path = pg_catalog;
SET LOCAL statement_timeout = '30s';
SET LOCAL lock_timeout = '5s';
WITH targets AS (
  SELECT 'catalogo.producto'::regclass AS oid, 'catalogo.producto' AS name
  UNION ALL SELECT 'compras.movimiento_inventario'::regclass, 'compras.movimiento_inventario'
), relations AS (
  SELECT t.name, 'constraint:'||c.conname AS item,
    jsonb_build_array(pg_get_constraintdef(c.oid,true),c.convalidated,c.condeferrable,c.condeferred)::text AS definition
  FROM targets t JOIN pg_constraint c ON c.conrelid=t.oid
  UNION ALL
  SELECT t.name, 'index:'||x.relname,
    jsonb_build_array(pg_get_indexdef(i.indexrelid),i.indisvalid,i.indisready)::text
  FROM targets t JOIN pg_index i ON i.indrelid=t.oid JOIN pg_class x ON x.oid=i.indexrelid
  UNION ALL
  SELECT t.name, 'trigger:'||g.tgname,
    jsonb_build_array(pg_get_triggerdef(g.oid,true),g.tgenabled)::text
  FROM targets t JOIN pg_trigger g ON g.tgrelid=t.oid AND NOT g.tgisinternal
)
SELECT json_build_object('database',current_database(),'username',current_user,
  'readOnly',current_setting('transaction_read_only'),'relation',t.name,
  'relationsHash',encode(sha256(convert_to(coalesce(string_agg(jsonb_build_array(r.item,r.definition)::text,''
    ORDER BY r.item COLLATE "C"),''),'UTF8')),'hex'),
  'items',json_agg(json_build_object('item',r.item,'definition',r.definition)
    ORDER BY r.item COLLATE "C"))
FROM targets t LEFT JOIN relations r ON r.name=t.name GROUP BY t.name ORDER BY t.name;
ROLLBACK;
