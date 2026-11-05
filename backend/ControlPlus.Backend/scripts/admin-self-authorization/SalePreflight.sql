BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_anulacion_venta_usuarios'
                    AND conrelid = 'ventas.anulacion_venta'::regclass)
     OR EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = '20261104010000_AdminSelfSaleCancellationV1')
     OR to_regprocedure('seguridad.puede_autoautorizar_administrador(uuid,text)') IS NOT NULL
  THEN RAISE EXCEPTION 'Sale self authorization preflight rejected'; END IF;
END $check$;
SELECT 'SALE_SELF_PREFLIGHT_OK' AS status;
ROLLBACK;
