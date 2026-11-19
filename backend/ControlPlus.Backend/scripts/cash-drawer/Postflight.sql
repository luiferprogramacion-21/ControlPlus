\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT CASE WHEN count(*) = 29
  AND max("MigrationId") = '20261119013000_HeldSalesV1'
  AND count(*) FILTER (WHERE "MigrationId" = '20261119010000_CashDrawerV1') = 1
  THEN 'history|29|20261119013000_HeldSalesV1' ELSE 'history|INVALID' END
FROM public."__EFMigrationsHistory";
-- Ventas en espera: ya no hay un solo borrador abierto por terminal.
SELECT CASE WHEN to_regclass('ventas.uq_borrador_venta_activo_terminal') IS NULL
  AND to_regclass('ventas.ix_borrador_venta_abiertos_usuario') IS NOT NULL
  THEN 'drafts|OK' ELSE 'drafts|INVALID' END;
SELECT 'profiles|' || count(*) || '|' ||
  md5(coalesce(string_agg((to_jsonb(p) - 'cajon_habilitado' - 'cajon_pin'
    - 'cajon_pulso_on_ms' - 'cajon_pulso_off_ms')::text, '|' ORDER BY p.id), ''))
FROM configuracion.perfil_impresora p;
SELECT 'jobs|' || count(*) || '|' ||
  md5(coalesce(string_agg(to_jsonb(j)::text, '|' ORDER BY j.id), ''))
FROM configuracion.trabajo_impresion j;
SELECT CASE WHEN
  (SELECT count(*) FROM information_schema.columns
   WHERE table_schema='configuracion' AND table_name='perfil_impresora'
     AND ((column_name='cajon_habilitado' AND data_type='boolean' AND is_nullable='NO'
           AND column_default IN ('false','false::boolean'))
       OR (column_name='cajon_pin' AND data_type='smallint' AND is_nullable='YES')
       OR (column_name='cajon_pulso_on_ms' AND data_type='smallint' AND is_nullable='NO'
           AND column_default IN ('100','100::smallint'))
       OR (column_name='cajon_pulso_off_ms' AND data_type='smallint' AND is_nullable='NO'
           AND column_default IN ('100','100::smallint')))) = 4
  AND NOT EXISTS (SELECT 1 FROM configuracion.perfil_impresora
                  WHERE cajon_habilitado OR cajon_pin IS NOT NULL
                    OR cajon_pulso_on_ms <> 100 OR cajon_pulso_off_ms <> 100)
  THEN 'settings|DISABLED' ELSE 'settings|INVALID' END;
SELECT CASE WHEN to_regclass('caja.apertura_cajon') IS NOT NULL
  AND (SELECT count(*) FROM caja.apertura_cajon) = 0
  -- Por nombre: PostgreSQL 18 también guarda los NOT NULL en pg_constraint.
  AND (SELECT count(*) FROM pg_constraint
       WHERE conrelid = 'caja.apertura_cajon'::regclass AND convalidated
         AND conname IN ('pk_apertura_cajon','fk_apertura_cajon_perfil',
                         'fk_apertura_cajon_terminal','fk_apertura_cajon_turno',
                         'fk_apertura_cajon_usuario','ck_apertura_cajon_tipo',
                         'ck_apertura_cajon_estado','ck_apertura_cajon_perfil_estado',
                         'ck_apertura_cajon_motivo_manual','uq_apertura_cajon_operacion')) = 10
  AND (SELECT count(*) FROM pg_constraint
       WHERE conrelid = 'configuracion.perfil_impresora'::regclass
         AND conname IN ('ck_perfil_impresora_cajon_pin',
                         'ck_perfil_impresora_cajon_pulsos',
                         'ck_perfil_impresora_cajon_habilitado') AND convalidated) = 3
  THEN 'constraints|OK' ELSE 'constraints|INVALID' END;
SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app')
  AND has_table_privilege('controlplus_app','caja.apertura_cajon','SELECT')
  AND has_table_privilege('controlplus_app','caja.apertura_cajon','INSERT')
  AND has_table_privilege('controlplus_app','caja.apertura_cajon','UPDATE')
  AND has_table_privilege('controlplus_app','caja.apertura_cajon','DELETE')
  THEN 'privileges|OK' ELSE 'privileges|INVALID' END;
SELECT 'CASH_DRAWER_POSTFLIGHT_OK';
ROLLBACK;
