\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
DO $check$
DECLARE sequence_value bigint; sequence_used boolean; max_reference bigint;
BEGIN
  IF current_database() <> 'controlplus'
     AND current_database() !~ '^controlplus_test_[a-f0-9]{32}$' THEN
    RAISE EXCEPTION 'Unexpected database';
  END IF;
  IF current_setting('transaction_read_only')<>'on' THEN RAISE EXCEPTION 'Read-only context required'; END IF;
  IF NOT (coalesce((SELECT rolsuper FROM pg_roles WHERE rolname=current_user),false)
      OR pg_has_role(current_user,'pg_read_all_stats','USAGE')) THEN
    RAISE EXCEPTION 'Partial connection visibility; absence of other clients cannot be certified';
  END IF;
  IF (SELECT count(*) FROM public."__EFMigrationsHistory"
      WHERE "MigrationId"='20261029010000_PermanentProductReferencesV1')<>1 THEN
    RAISE EXCEPTION 'Target history mismatch';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database()
      AND pid<>pg_backend_pid() AND backend_type='client backend') THEN
    RAISE EXCEPTION 'Other database sessions: maintenance window required';
  END IF;
  IF EXISTS (SELECT 1 FROM catalogo.producto WHERE codigo_interno !~ '^P-[0-9]{6,}$') THEN
    RAISE EXCEPTION 'Invalid reference shape';
  END IF;
  IF EXISTS (SELECT 1 FROM catalogo.producto GROUP BY codigo_interno HAVING count(*)>1) THEN
    RAISE EXCEPTION 'Duplicate reference';
  END IF;
  IF EXISTS (SELECT 1 FROM catalogo.producto p WHERE
      NOT EXISTS (SELECT 1 FROM catalogo.producto_codigo c WHERE c.codigo=p.codigo_interno AND c.producto_id=p.id)
      OR (p.codigo_barras IS NOT NULL AND NOT EXISTS (SELECT 1 FROM catalogo.producto_codigo c
          WHERE c.codigo=upper(btrim(p.codigo_barras)) AND c.producto_id=p.id))) THEN
    RAISE EXCEPTION 'Reference/barcode alias mismatch';
  END IF;
  IF EXISTS (SELECT 1 FROM catalogo.producto_codigo c WHERE codigo<>upper(btrim(codigo)) OR codigo=''
      OR NOT EXISTS (SELECT 1 FROM catalogo.producto p WHERE p.id=c.producto_id)) THEN
    RAISE EXCEPTION 'Invalid alias';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='catalogo.producto_codigo'::regclass AND contype='p')
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='catalogo.producto_codigo'::regclass
       AND contype='f' AND confrelid='catalogo.producto'::regclass AND confdeltype='r' AND convalidated)
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='catalogo.producto_codigo'::regclass
       AND conname='ck_producto_codigo_normalizado' AND convalidated)
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='catalogo.producto'::regclass
       AND conname='ck_producto_formato_codigo' AND convalidated) THEN
    RAISE EXCEPTION 'Registry constraints missing';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
      WHERE i.indrelid='catalogo.producto_codigo'::regclass AND c.relname='ix_producto_codigo_producto'
      AND i.indisvalid AND i.indisready) THEN RAISE EXCEPTION 'Registry index missing'; END IF;
  IF (SELECT count(*) FROM pg_trigger WHERE tgrelid='catalogo.producto'::regclass
      AND NOT tgisinternal AND tgenabled='O' AND (tgname,tgfoid) IN (
       ('trg_producto_referencia_permanente','catalogo.proteger_codigos_producto()'::regprocedure),
       ('trg_producto_registrar_codigos','catalogo.registrar_codigos_producto()'::regprocedure)))<>2 THEN
    RAISE EXCEPTION 'Product triggers missing';
  END IF;
  IF to_regprocedure('catalogo.siguiente_referencia_producto()') IS NULL
     OR to_regprocedure('catalogo.reservar_codigo_producto(text,uuid)') IS NULL THEN
    RAISE EXCEPTION 'Registry functions missing';
  END IF;
  SELECT last_value,is_called INTO sequence_value,sequence_used FROM catalogo.producto_referencia_seq;
  SELECT max(substring(codigo_interno FROM 3)::bigint) INTO max_reference FROM catalogo.producto;
  IF max_reference IS NOT NULL AND (NOT sequence_used OR sequence_value<max_reference) THEN
    RAISE EXCEPTION 'Reference sequence behind products';
  END IF;
END $check$;
SELECT conname,pg_get_constraintdef(oid,true) AS definition FROM pg_constraint
WHERE conrelid IN ('catalogo.producto'::regclass,'catalogo.producto_codigo'::regclass) ORDER BY conname;
SELECT tgname,pg_get_triggerdef(oid,true) AS definition FROM pg_trigger
WHERE tgrelid='catalogo.producto'::regclass AND NOT tgisinternal ORDER BY tgname;
SELECT p.proname,pg_get_functiondef(p.oid) AS definition FROM pg_proc p
JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='catalogo' AND p.proname IN (
  'siguiente_referencia_producto','reservar_codigo_producto','proteger_codigos_producto','registrar_codigos_producto')
ORDER BY p.proname;
SELECT count(*) AS products,count(DISTINCT codigo_interno) AS unique_references FROM catalogo.producto;
SELECT count(*) AS aliases FROM catalogo.producto_codigo;
SELECT 'POSTFLIGHT_SQL_OK';
ROLLBACK;
