BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;

DO $guard$
DECLARE
    v_history_count integer;
    v_last_migration text;
    v_block record;
    v_limit record;
    v_check text;
BEGIN
    SELECT count(*), max("MigrationId")
      INTO v_history_count, v_last_migration
      FROM public."__EFMigrationsHistory";

    SELECT data_type, is_nullable, column_default
      INTO v_block
      FROM information_schema.columns
     WHERE table_schema = 'configuracion' AND table_name = 'establecimiento' AND column_name = 'credito_bloquear_riesgo';

    SELECT data_type, is_nullable, numeric_precision, numeric_scale
      INTO v_limit
      FROM information_schema.columns
     WHERE table_schema = 'catalogo' AND table_name = 'cliente' AND column_name = 'cupo_credito';

    SELECT replace(replace(pg_get_constraintdef(oid), '(', ''), ')', '')
      INTO v_check
      FROM pg_constraint
     WHERE conname = 'ck_cliente_cupo_credito'
       AND conrelid = 'catalogo.cliente'::regclass;

    IF current_database() <> 'controlplus'
       OR current_setting('transaction_read_only') <> 'on'
       OR v_history_count <> 26
       OR v_last_migration <> '20261116010000_CreditRiskControlsV1'
       OR v_block IS NULL
       OR v_block.data_type <> 'boolean'
       OR v_block.is_nullable <> 'NO'
       OR v_block.column_default IS DISTINCT FROM 'false'
       OR v_limit IS NULL
       OR v_limit.data_type <> 'numeric'
       OR v_limit.is_nullable <> 'YES'
       OR v_limit.numeric_precision <> 18
       OR v_limit.numeric_scale <> 0
       OR v_check IS NULL
       OR v_check NOT LIKE '%cupo_credito IS NULL%'
       OR v_check NOT LIKE '%cupo_credito > 0%'
       OR EXISTS (SELECT 1 FROM configuracion.establecimiento WHERE credito_bloquear_riesgo)
       OR EXISTS (SELECT 1 FROM catalogo.cliente WHERE cupo_credito IS NOT NULL)
       OR (EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
           AND NOT (has_column_privilege('controlplus_app', 'configuracion.establecimiento', 'credito_bloquear_riesgo', 'SELECT')
                    AND has_column_privilege('controlplus_app', 'configuracion.establecimiento', 'credito_bloquear_riesgo', 'UPDATE')
                    AND has_column_privilege('controlplus_app', 'catalogo.cliente', 'cupo_credito', 'SELECT')
                    AND has_column_privilege('controlplus_app', 'catalogo.cliente', 'cupo_credito', 'INSERT')
                    AND has_column_privilege('controlplus_app', 'catalogo.cliente', 'cupo_credito', 'UPDATE')))
    THEN
        RAISE EXCEPTION 'CREDIT_RISK_POSTFLIGHT_REJECTED';
    END IF;
END
$guard$;

SELECT 'history|' || count(*)::text || '|' || max("MigrationId")
FROM public."__EFMigrationsHistory";

SELECT 'establishments|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5((to_jsonb(e) - 'credito_bloquear_riesgo')::text), '' ORDER BY e.id::text)), md5(''))
FROM configuracion.establecimiento e;

SELECT 'customers|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5((to_jsonb(c) - 'cupo_credito')::text), '' ORDER BY c.id::text)), md5(''))
FROM catalogo.cliente c;

SELECT 'CREDIT_RISK_POSTFLIGHT_OK';
ROLLBACK;
