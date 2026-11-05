BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261104020000_AdminSelfCashDifferenceV1')
     OR EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = '20261105010000_SupervisorLayawayRefundDecisionV1')
     OR NOT EXISTS (SELECT 1 FROM seguridad.rol WHERE codigo = 'SUPERVISOR' AND activo)
     OR NOT EXISTS (SELECT 1 FROM seguridad.permiso
                    WHERE codigo = 'LAYAWAY.REFUND_DECIDE' AND activo)
     OR EXISTS (SELECT 1 FROM seguridad.rol_permiso rp
                JOIN seguridad.rol r ON r.id = rp.rol_id
                JOIN seguridad.permiso p ON p.id = rp.permiso_id
                WHERE r.codigo = 'SUPERVISOR' AND p.codigo = 'LAYAWAY.REFUND_DECIDE')
  THEN RAISE EXCEPTION 'Supervisor refund decision preflight rejected'; END IF;
END $check$;
SELECT 'SUPERVISOR_REFUND_PREFLIGHT_OK' AS status;
ROLLBACK;
