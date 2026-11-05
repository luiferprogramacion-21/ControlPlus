BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $check$ BEGIN
  IF current_setting('transaction_read_only') <> 'on'
     OR (SELECT count(*) FROM "__EFMigrationsHistory"
         WHERE "MigrationId" = '20261105010000_SupervisorLayawayRefundDecisionV1') <> 1
     OR (SELECT count(*) FROM seguridad.rol_permiso rp
         JOIN seguridad.rol r ON r.id = rp.rol_id
         JOIN seguridad.permiso p ON p.id = rp.permiso_id
         WHERE r.codigo = 'SUPERVISOR' AND r.activo
           AND p.codigo = 'LAYAWAY.REFUND_DECIDE' AND p.activo) <> 1
  THEN RAISE EXCEPTION 'Supervisor refund decision postflight rejected'; END IF;
END $check$;
SELECT 'SUPERVISOR_REFUND_POSTFLIGHT_OK' AS status;
ROLLBACK;
