BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_anulacion_venta_usuarios'
                 AND conrelid = 'ventas.anulacion_venta'::regclass)
     OR NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'tg_anulacion_venta_validar_admin'
                    AND tgrelid = 'ventas.anulacion_venta'::regclass AND NOT tgisinternal)
     OR to_regprocedure('seguridad.puede_autoautorizar_administrador(uuid,text)') IS NULL
     OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261104010000_AdminSelfSaleCancellationV1')
  THEN RAISE EXCEPTION 'Sale self authorization postflight rejected'; END IF;
END $check$;
SELECT 'SALE_SELF_POSTFLIGHT_OK' AS status;
ROLLBACK;
