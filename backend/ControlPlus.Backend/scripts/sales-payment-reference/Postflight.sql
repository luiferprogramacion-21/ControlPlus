BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;

DO $guard$
DECLARE
    v_history_count integer;
    v_last_migration text;
    v_sale_trigger_function text;
    v_credit_trigger_function text;
    v_layaway_trigger_function text;
    v_sale_function_definition text;
BEGIN
    SELECT count(*), max("MigrationId")
      INTO v_history_count, v_last_migration
      FROM public."__EFMigrationsHistory";

    SELECT p.proname
      INTO v_sale_trigger_function
      FROM pg_trigger t
      JOIN pg_proc p ON p.oid = t.tgfoid
     WHERE t.tgrelid = 'ventas.pago_venta'::regclass
       AND t.tgname = 'tg_pago_venta_validar_datos'
       AND NOT t.tgisinternal;

    SELECT p.proname
      INTO v_credit_trigger_function
      FROM pg_trigger t
      JOIN pg_proc p ON p.oid = t.tgfoid
     WHERE t.tgrelid = 'ventas.pago_credito'::regclass
       AND t.tgname = 'tg_pago_credito_validar_datos'
       AND NOT t.tgisinternal;

    SELECT p.proname
      INTO v_layaway_trigger_function
      FROM pg_trigger t
      JOIN pg_proc p ON p.oid = t.tgfoid
     WHERE t.tgrelid = 'ventas.pago_apartado'::regclass
       AND t.tgname = 'tg_pago_apartado_validar_datos'
       AND NOT t.tgisinternal;

    SELECT pg_get_functiondef('ventas.validar_datos_pago_venta()'::regprocedure)
      INTO v_sale_function_definition;

    IF current_database() <> 'controlplus'
       OR current_setting('transaction_read_only') <> 'on'
       OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999
       OR v_history_count <> 24
       OR v_last_migration <> '20261115010000_OptionalSalePaymentReferenceV1'
       OR v_sale_trigger_function IS DISTINCT FROM 'validar_datos_pago_venta'
       OR v_credit_trigger_function IS DISTINCT FROM 'validar_datos_cobro'
       OR v_layaway_trigger_function IS DISTINCT FROM 'validar_datos_cobro'
       OR v_sale_function_definition NOT LIKE '%La referencia opcional no puede estar vacia.%'
       OR v_sale_function_definition LIKE '%requiere_referencia%'
    THEN
        RAISE EXCEPTION 'OPTIONAL_SALE_PAYMENT_REFERENCE_POSTFLIGHT_REJECTED';
    END IF;
END
$guard$;

SELECT 'history|' || count(*)::text || '|' || max("MigrationId")
FROM public."__EFMigrationsHistory";

SELECT 'payments|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5(row_to_json(p)::text), '' ORDER BY p.id::text)), md5(''))
FROM ventas.pago_venta p;

SELECT 'OPTIONAL_SALE_PAYMENT_REFERENCE_POSTFLIGHT_OK';
ROLLBACK;
