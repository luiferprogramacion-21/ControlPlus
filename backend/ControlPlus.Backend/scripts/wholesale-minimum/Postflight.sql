BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;

DO $guard$
DECLARE
    v_history_count integer;
    v_last_migration text;
    v_column record;
    v_check text;
BEGIN
    SELECT count(*), max("MigrationId")
      INTO v_history_count, v_last_migration
      FROM public."__EFMigrationsHistory";

    SELECT data_type, is_nullable, column_default
      INTO v_column
      FROM information_schema.columns
     WHERE table_schema = 'configuracion'
       AND table_name = 'establecimiento'
       AND column_name = 'minimo_unidades_mayorista';

    SELECT replace(replace(pg_get_constraintdef(oid), '(', ''), ')', '')
      INTO v_check
      FROM pg_constraint
     WHERE conname = 'ck_establecimiento_minimo_unidades_mayorista'
       AND conrelid = 'configuracion.establecimiento'::regclass;

    IF current_database() <> 'controlplus'
       OR current_setting('transaction_read_only') <> 'on'
       OR v_history_count <> 25
       OR v_last_migration <> '20261115020000_ConfigurableWholesaleMinimumV1'
       OR v_column IS NULL
       OR v_column.data_type <> 'integer'
       OR v_column.is_nullable <> 'NO'
       OR v_column.column_default IS DISTINCT FROM '12'
       OR v_check IS NULL
       OR v_check NOT LIKE '%minimo_unidades_mayorista >= 1%'
       OR v_check NOT LIKE '%minimo_unidades_mayorista <= 999%'
       OR EXISTS (SELECT 1 FROM configuracion.establecimiento WHERE minimo_unidades_mayorista <> 12)
       OR (EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
           AND NOT (has_column_privilege('controlplus_app', 'configuracion.establecimiento',
                                         'minimo_unidades_mayorista', 'SELECT')
                    AND has_column_privilege('controlplus_app', 'configuracion.establecimiento',
                                             'minimo_unidades_mayorista', 'UPDATE')))
    THEN
        RAISE EXCEPTION 'WHOLESALE_MINIMUM_POSTFLIGHT_REJECTED';
    END IF;
END
$guard$;

SELECT 'history|' || count(*)::text || '|' || max("MigrationId")
FROM public."__EFMigrationsHistory";

SELECT 'establishments|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5((to_jsonb(e) - 'minimo_unidades_mayorista')::text), '' ORDER BY e.id::text)), md5(''))
FROM configuracion.establecimiento e;

SELECT 'WHOLESALE_MINIMUM_POSTFLIGHT_OK';
ROLLBACK;
