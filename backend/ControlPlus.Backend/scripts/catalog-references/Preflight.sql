\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL TIME ZONE 'UTC';
DO $check$
BEGIN
  IF current_database() <> 'controlplus'
     AND current_database() !~ '^controlplus_test_[a-f0-9]{32}$' THEN
    RAISE EXCEPTION 'Unexpected database';
  END IF;
  IF current_setting('server_version_num')::int NOT BETWEEN 180000 AND 189999 THEN
    RAISE EXCEPTION 'PostgreSQL 18 required';
  END IF;
  IF current_setting('transaction_read_only') <> 'on' THEN RAISE EXCEPTION 'Read-only context required'; END IF;
  IF NOT (coalesce((SELECT rolsuper FROM pg_roles WHERE rolname=current_user),false)
      OR pg_has_role(current_user,'pg_read_all_stats','USAGE')) THEN
    RAISE EXCEPTION 'Partial connection visibility; absence of other clients cannot be certified';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database()
      AND pid<>pg_backend_pid() AND backend_type='client backend') THEN
    RAISE EXCEPTION 'Other database sessions: maintenance window required';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='catalogo.producto'::regclass
      AND conname='ck_producto_formato_codigo' AND contype='c' AND convalidated) THEN
    RAISE EXCEPTION 'Validated barcode constraint missing';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid='catalogo.producto'::regclass
      AND NOT tgisinternal AND tgenabled <> 'D') THEN
    RAISE EXCEPTION 'Existing product trigger needs separate review';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_rewrite WHERE ev_class='catalogo.producto'::regclass AND rulename<>'_RETURN') THEN
    RAISE EXCEPTION 'Existing product rule needs separate review';
  END IF;
END $check$;
SELECT inet_server_addr()::text AS servidor,inet_server_port() AS puerto,current_user AS usuario,current_database() AS base;
SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT conname,pg_get_constraintdef(oid,true) AS definition
FROM pg_constraint WHERE conrelid='catalogo.producto'::regclass ORDER BY conname;
SELECT 'PREFLIGHT_SQL_OK';
ROLLBACK;
