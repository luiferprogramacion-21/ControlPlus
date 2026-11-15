BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;

DO $guard$
DECLARE
    v_history_count integer;
    v_last_migration text;
    v_trigger_function text;
BEGIN
    SELECT count(*), max("MigrationId")
      INTO v_history_count, v_last_migration
      FROM public."__EFMigrationsHistory";

    SELECT p.proname
      INTO v_trigger_function
      FROM pg_trigger t
      JOIN pg_proc p ON p.oid = t.tgfoid
     WHERE t.tgrelid = 'ventas.pago_venta'::regclass
       AND t.tgname = 'tg_pago_venta_validar_datos'
       AND NOT t.tgisinternal;

    IF current_database() <> 'controlplus'
       OR current_setting('transaction_read_only') <> 'on'
       OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999
       OR v_history_count <> 23
       OR v_last_migration <> '20261112010000_LayawayMinimumInitialPaymentV1'
       OR EXISTS (
            SELECT 1
              FROM pg_stat_activity
             WHERE datname = current_database()
               AND pid <> pg_backend_pid()
               AND backend_type = 'client backend')
       OR EXISTS (
            SELECT 1
              FROM public."__EFMigrationsHistory"
             WHERE "MigrationId" = '20261115010000_OptionalSalePaymentReferenceV1')
       OR v_trigger_function IS DISTINCT FROM 'validar_datos_cobro'
       OR to_regprocedure('ventas.validar_datos_pago_venta()') IS NOT NULL
    THEN
        RAISE EXCEPTION 'OPTIONAL_SALE_PAYMENT_REFERENCE_PREFLIGHT_REJECTED';
    END IF;
END
$guard$;

SELECT 'history|' || count(*)::text || '|' || max("MigrationId")
FROM public."__EFMigrationsHistory";

SELECT 'payments|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5(row_to_json(p)::text), '' ORDER BY p.id::text)), md5(''))
FROM ventas.pago_venta p;

SELECT 'clients|' || count(*)::text
FROM pg_stat_activity
WHERE datname = current_database()
  AND pid <> pg_backend_pid()
  AND backend_type = 'client backend';

SELECT 'OPTIONAL_SALE_PAYMENT_REFERENCE_PREFLIGHT_OK';
ROLLBACK;
