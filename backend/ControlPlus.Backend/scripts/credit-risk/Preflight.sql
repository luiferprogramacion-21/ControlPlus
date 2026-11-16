BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;

DO $guard$
DECLARE
    v_history_count integer;
    v_last_migration text;
BEGIN
    SELECT count(*), max("MigrationId")
      INTO v_history_count, v_last_migration
      FROM public."__EFMigrationsHistory";

    IF current_database() <> 'controlplus'
       OR current_setting('transaction_read_only') <> 'on'
       OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999
       OR v_history_count <> 25
       OR v_last_migration <> '20261115020000_ConfigurableWholesaleMinimumV1'
       OR EXISTS (
            SELECT 1
              FROM pg_stat_activity
             WHERE datname = current_database()
               AND pid <> pg_backend_pid()
               AND backend_type = 'client backend')
       OR EXISTS (
            SELECT 1
              FROM public."__EFMigrationsHistory"
             WHERE "MigrationId" = '20261116010000_CreditRiskControlsV1')
       OR EXISTS (
            SELECT 1
              FROM information_schema.columns
             WHERE (table_schema = 'configuracion' AND table_name = 'establecimiento' AND column_name = 'credito_bloquear_riesgo')
                OR (table_schema = 'catalogo' AND table_name = 'cliente' AND column_name = 'cupo_credito'))
       OR EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_cliente_cupo_credito')
    THEN
        RAISE EXCEPTION 'CREDIT_RISK_PREFLIGHT_REJECTED';
    END IF;
END
$guard$;

SELECT 'history|' || count(*)::text || '|' || max("MigrationId")
FROM public."__EFMigrationsHistory";

-- Huellas sin las columnas nuevas: deben ser idénticas antes y después.
SELECT 'establishments|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5((to_jsonb(e) - 'credito_bloquear_riesgo')::text), '' ORDER BY e.id::text)), md5(''))
FROM configuracion.establecimiento e;

SELECT 'customers|' || count(*)::text || '|' ||
       coalesce(md5(string_agg(md5((to_jsonb(c) - 'cupo_credito')::text), '' ORDER BY c.id::text)), md5(''))
FROM catalogo.cliente c;

SELECT 'clients|' || count(*)::text
FROM pg_stat_activity
WHERE datname = current_database()
  AND pid <> pg_backend_pid()
  AND backend_type = 'client backend';

SELECT 'CREDIT_RISK_PREFLIGHT_OK';
ROLLBACK;
