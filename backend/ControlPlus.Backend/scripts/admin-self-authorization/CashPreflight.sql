BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261104010000_AdminSelfSaleCancellationV1')
     OR EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = '20261104020000_AdminSelfCashDifferenceV1')
     OR position('OR v_autorizacion.usuario_autorizador_id IS NOT DISTINCT FROM NEW.usuario_ejecutor_id'
                 IN pg_get_functiondef('caja.vincular_autorizacion_cierre()'::regprocedure)) = 0
     OR position('AND a.usuario_autorizador_id <> v_turno.usuario_cierre_id'
                 IN pg_get_functiondef('caja.validar_integridad_arqueo(uuid)'::regprocedure)) = 0
  THEN RAISE EXCEPTION 'Cash self authorization preflight rejected'; END IF;
END $check$;
SELECT 'CASH_SELF_PREFLIGHT_OK' AS status;
ROLLBACK;
