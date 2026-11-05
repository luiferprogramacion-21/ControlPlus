BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261104020000_AdminSelfCashDifferenceV1')
     OR position('seguridad.puede_autoautorizar_administrador(NEW.usuario_ejecutor_id'
                 IN pg_get_functiondef('caja.vincular_autorizacion_cierre()'::regprocedure)) = 0
     OR position('seguridad.puede_autoautorizar_administrador(v_turno.usuario_cierre_id'
                 IN pg_get_functiondef('caja.validar_integridad_arqueo(uuid)'::regprocedure)) = 0
  THEN RAISE EXCEPTION 'Cash self authorization postflight rejected'; END IF;
END $check$;
SELECT 'CASH_SELF_POSTFLIGHT_OK' AS status;
ROLLBACK;
