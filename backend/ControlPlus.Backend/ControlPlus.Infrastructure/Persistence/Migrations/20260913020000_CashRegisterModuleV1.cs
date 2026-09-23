using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20260913020000_CashRegisterModuleV1")]
public sealed class CashRegisterModuleV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Lock and inspect before changing any schema or catalog data. The lock
            -- prevents an opening racing the precondition in another transaction.
            LOCK TABLE caja.turno_caja IN ACCESS EXCLUSIVE MODE;
            DO $precondicion$
            BEGIN
                IF EXISTS (SELECT 1 FROM caja.turno_caja) THEN
                    RAISE EXCEPTION 'CashRegisterModuleV1 requiere ausencia de turnos previos; se necesita una migracion de transicion especifica.';
                END IF;
            END;
            $precondicion$;

            -- Preserve the Phase 4 cash-only ck_turno_caja_diferencia unchanged.
            ALTER TABLE caja.turno_caja
                ADD COLUMN diferencia_total numeric(18,0) NULL,
                DROP CONSTRAINT ck_turno_caja_motivo_diferencia,
                ADD CONSTRAINT ck_turno_caja_motivo_diferencia CHECK (
                    diferencia_total IS NULL
                    OR (diferencia_total = 0 AND motivo_diferencia_id IS NULL)
                    OR (diferencia_total <> 0 AND motivo_diferencia_id IS NOT NULL)
                ),
                ADD CONSTRAINT ck_turno_caja_diferencia_total CHECK (
                    (estado = 'ABIERTA' AND diferencia_total IS NULL)
                    OR (estado = 'CERRADA' AND diferencia_total IS NOT NULL)
                );

            CREATE TABLE caja.autorizacion_cierre_turno (
                turno_caja_id uuid NOT NULL,
                autorizacion_id uuid NOT NULL,
                correlacion_id uuid NOT NULL,
                usuario_ejecutor_id uuid NOT NULL,
                fecha_vinculacion timestamptz NOT NULL,
                instalacion_id uuid NOT NULL,
                establecimiento_id uuid NOT NULL,
                CONSTRAINT pk_autorizacion_cierre_turno PRIMARY KEY (turno_caja_id),
                CONSTRAINT uq_autorizacion_cierre_autorizacion UNIQUE (autorizacion_id),
                CONSTRAINT fk_autorizacion_cierre_turno FOREIGN KEY (turno_caja_id)
                    REFERENCES caja.turno_caja(id) ON DELETE RESTRICT,
                CONSTRAINT fk_autorizacion_cierre_autorizacion FOREIGN KEY (autorizacion_id)
                    REFERENCES seguridad.autorizacion_operacion(id) ON DELETE RESTRICT,
                CONSTRAINT fk_autorizacion_cierre_ejecutor FOREIGN KEY (usuario_ejecutor_id)
                    REFERENCES seguridad.usuario(id) ON DELETE RESTRICT,
                CONSTRAINT fk_autorizacion_cierre_instalacion FOREIGN KEY (instalacion_id)
                    REFERENCES configuracion.instalacion(id) ON DELETE RESTRICT,
                CONSTRAINT fk_autorizacion_cierre_establecimiento FOREIGN KEY (establecimiento_id)
                    REFERENCES configuracion.establecimiento(id) ON DELETE RESTRICT
            );

            CREATE TABLE caja.detalle_arqueo_medio_pago (
                id uuid NOT NULL,
                turno_caja_id uuid NOT NULL,
                metodo_pago_id uuid NOT NULL,
                valor_esperado numeric(18,0) NOT NULL,
                valor_contado numeric(18,0) NOT NULL,
                diferencia numeric(18,0) NOT NULL,
                CONSTRAINT pk_detalle_arqueo_medio_pago PRIMARY KEY (id),
                CONSTRAINT fk_detalle_arqueo_turno
                    FOREIGN KEY (turno_caja_id) REFERENCES caja.turno_caja(id) ON DELETE RESTRICT,
                CONSTRAINT fk_detalle_arqueo_metodo_pago
                    FOREIGN KEY (metodo_pago_id) REFERENCES catalogo.metodo_pago(id) ON DELETE RESTRICT,
                CONSTRAINT uq_detalle_arqueo_turno_metodo UNIQUE (turno_caja_id, metodo_pago_id),
                CONSTRAINT ck_detalle_arqueo_valores CHECK (
                    diferencia = valor_contado - valor_esperado
                )
            );

            CREATE INDEX ix_detalle_arqueo_metodo_pago
                ON caja.detalle_arqueo_medio_pago(metodo_pago_id);

            -- One cash movement may group several payment-method amounts.
            ALTER TABLE ventas.pago_venta DROP CONSTRAINT uq_pago_venta_movimiento_caja;
            ALTER TABLE ventas.pago_credito DROP CONSTRAINT uq_pago_credito_movimiento_caja;
            ALTER TABLE ventas.pago_apartado DROP CONSTRAINT uq_pago_apartado_movimiento_caja;
            ALTER TABLE ventas.pago_cambio_venta DROP CONSTRAINT uq_pago_cambio_movimiento_caja;
            ALTER TABLE ventas.reembolso_apartado DROP CONSTRAINT uq_reembolso_apartado_movimiento;
            CREATE INDEX ix_pago_venta_movimiento_caja ON ventas.pago_venta(movimiento_caja_id);
            CREATE INDEX ix_pago_credito_movimiento_caja ON ventas.pago_credito(movimiento_caja_id);
            CREATE INDEX ix_pago_apartado_movimiento_caja ON ventas.pago_apartado(movimiento_caja_id);
            CREATE INDEX ix_pago_cambio_movimiento_caja ON ventas.pago_cambio_venta(movimiento_caja_id);
            CREATE INDEX ix_reembolso_apartado_movimiento ON ventas.reembolso_apartado(movimiento_caja_id);

            INSERT INTO catalogo.metodo_pago
                (id, codigo, nombre, afecta_efectivo, requiere_referencia, orden_visual, activo)
            VALUES
                (md5('controlplus.payment-method.EFECTIVO')::uuid,
                 'EFECTIVO', 'Efectivo', true, false, 10, true),
                (md5('controlplus.payment-method.TRANSFERENCIA')::uuid,
                 'TRANSFERENCIA', 'Transferencia', false, true, 20, true),
                (md5('controlplus.payment-method.NEQUI')::uuid,
                 'NEQUI', 'Nequi', false, true, 30, true)
            ON CONFLICT (codigo) DO NOTHING;

            CREATE FUNCTION caja.proteger_detalle_arqueo()
            RETURNS trigger LANGUAGE plpgsql AS $funcion$
            DECLARE
                v_turno_id uuid;
                v_estado text;
                v_afecta_efectivo boolean;
            BEGIN
                IF TG_OP = 'UPDATE' AND (
                    NEW.id IS DISTINCT FROM OLD.id
                    OR NEW.turno_caja_id IS DISTINCT FROM OLD.turno_caja_id
                    OR NEW.metodo_pago_id IS DISTINCT FROM OLD.metodo_pago_id
                ) THEN
                    RAISE EXCEPTION 'La identidad del detalle de arqueo es inmutable.';
                END IF;
                v_turno_id := CASE WHEN TG_OP = 'DELETE' THEN OLD.turno_caja_id ELSE NEW.turno_caja_id END;
                SELECT estado INTO v_estado FROM caja.turno_caja
                WHERE id = v_turno_id FOR UPDATE;
                IF v_estado IS DISTINCT FROM 'ABIERTA' THEN
                    RAISE EXCEPTION 'No se puede modificar el arqueo de un turno cerrado.';
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                SELECT afecta_efectivo INTO v_afecta_efectivo FROM catalogo.metodo_pago
                WHERE id = NEW.metodo_pago_id FOR SHARE;
                IF v_afecta_efectivo AND (NEW.valor_esperado < 0 OR NEW.valor_contado < 0) THEN
                    RAISE EXCEPTION 'El saldo fisico de efectivo no puede ser negativo.' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END;
            $funcion$;

            CREATE TRIGGER tg_detalle_proteger_arqueo
            BEFORE INSERT OR UPDATE OR DELETE ON caja.detalle_arqueo_medio_pago
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_detalle_arqueo();

            CREATE FUNCTION caja.proteger_turno_cerrado()
            RETURNS trigger LANGUAGE plpgsql AS $funcion$
            BEGIN
                IF OLD.estado = 'CERRADA' THEN
                    RAISE EXCEPTION 'Un turno cerrado es historico e inmutable; no se puede reabrir.';
                END IF;
                IF TG_OP = 'UPDATE' AND (
                    NEW.id IS DISTINCT FROM OLD.id
                    OR NEW.caja_id IS DISTINCT FROM OLD.caja_id
                    OR NEW.terminal_id IS DISTINCT FROM OLD.terminal_id
                ) THEN
                    RAISE EXCEPTION 'La identidad y el contexto del turno son inmutables.';
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END;
            $funcion$;

            CREATE TRIGGER tg_turno_proteger_cierre
            BEFORE UPDATE OR DELETE ON caja.turno_caja
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_turno_cerrado();

            CREATE FUNCTION caja.proteger_metodo_arqueado()
            RETURNS trigger LANGUAGE plpgsql AS $funcion$
            BEGIN
                IF NEW.afecta_efectivo IS DISTINCT FROM OLD.afecta_efectivo
                   AND EXISTS (SELECT 1 FROM caja.detalle_arqueo_medio_pago WHERE metodo_pago_id = OLD.id) THEN
                    RAISE EXCEPTION 'No se puede cambiar el efecto en efectivo de un metodo con arqueos historicos.';
                END IF;
                RETURN NEW;
            END;
            $funcion$;

            CREATE TRIGGER tg_metodo_proteger_arqueo
            BEFORE UPDATE OF afecta_efectivo ON catalogo.metodo_pago
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_metodo_arqueado();

            CREATE FUNCTION caja.vincular_autorizacion_cierre()
            RETURNS trigger LANGUAGE plpgsql AS $funcion$
            DECLARE
                v_turno caja.turno_caja%ROWTYPE;
                v_autorizacion seguridad.autorizacion_operacion%ROWTYPE;
                v_ahora timestamptz := statement_timestamp();
            BEGIN
                IF TG_OP <> 'INSERT' THEN
                    RAISE EXCEPTION 'El vinculo de autorizacion de cierre es inmutable.';
                END IF;
                SELECT * INTO v_turno FROM caja.turno_caja WHERE id = NEW.turno_caja_id FOR UPDATE;
                IF NOT FOUND OR v_turno.estado <> 'ABIERTA' THEN
                    RAISE EXCEPTION 'La autorizacion debe vincularse a un turno abierto.';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM caja.caja c
                    JOIN configuracion.instalacion i ON i.id = c.instalacion_id
                    JOIN configuracion.terminal t ON t.id = v_turno.terminal_id
                    WHERE c.id = v_turno.caja_id AND i.id = NEW.instalacion_id
                        AND i.establecimiento_id = NEW.establecimiento_id
                        AND t.instalacion_id = i.id
                ) THEN
                    RAISE EXCEPTION 'El contexto de instalacion y establecimiento del cierre no coincide.';
                END IF;
                SELECT * INTO v_autorizacion FROM seguridad.autorizacion_operacion
                WHERE id = NEW.autorizacion_id FOR UPDATE;
                IF NOT FOUND
                   OR v_autorizacion.estado <> 'AUTORIZADA'
                   OR v_autorizacion.resultado IS DISTINCT FROM 'EXITOSA'
                   OR v_autorizacion.fecha_utilizacion IS NOT NULL
                   OR v_autorizacion.fecha_solicitud > v_ahora
                   OR v_autorizacion.fecha_expiracion <= v_ahora
                   OR v_autorizacion.tipo_operacion <> 'CIERRE_CAJA_DIFERENCIA'
                   OR v_autorizacion.correlacion_id <> NEW.correlacion_id
                   OR v_autorizacion.usuario_solicitante_id <> NEW.usuario_ejecutor_id
                   OR v_autorizacion.usuario_autorizador_id IS NOT DISTINCT FROM NEW.usuario_ejecutor_id
                   OR EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno WHERE autorizacion_id = NEW.autorizacion_id)
                THEN
                    RAISE EXCEPTION 'La autorizacion del cierre no esta aprobada, vigente o disponible para esta operacion.';
                END IF;
                IF NOT EXISTS (
                    SELECT 1 FROM seguridad.usuario u
                    JOIN seguridad.usuario_rol ur ON ur.usuario_id = u.id
                    JOIN seguridad.rol r ON r.id = ur.rol_id
                    JOIN seguridad.permiso p ON p.id = v_autorizacion.permiso_id
                    WHERE u.id = v_autorizacion.usuario_autorizador_id
                      AND u.activo AND u.bloqueo_hasta IS NULL
                      AND r.activo AND r.codigo IN ('SUPERVISOR', 'ADMINISTRADOR')
                      AND p.activo AND p.codigo = 'CASH.SHIFT_MANAGE'
                      AND NOT EXISTS (
                          SELECT 1 FROM seguridad.usuario_permiso up
                          WHERE up.usuario_id = u.id AND up.permiso_id = p.id AND up.efecto = 'REVOCAR')
                      AND (EXISTS (
                          SELECT 1 FROM seguridad.usuario_permiso up
                          WHERE up.usuario_id = u.id AND up.permiso_id = p.id AND up.efecto = 'CONCEDER')
                          OR EXISTS (SELECT 1 FROM seguridad.rol_permiso rp
                                     WHERE rp.rol_id = r.id AND rp.permiso_id = p.id))
                ) THEN
                    RAISE EXCEPTION 'El autorizador no tiene rol y permiso efectivos para autorizar el cierre.';
                END IF;
                -- Database time is authoritative: a caller cannot backdate consumption.
                NEW.fecha_vinculacion := v_ahora;
                UPDATE seguridad.autorizacion_operacion
                SET estado = 'UTILIZADA', fecha_utilizacion = v_ahora
                WHERE id = NEW.autorizacion_id;
                RETURN NEW;
            END;
            $funcion$;

            CREATE TRIGGER tg_autorizacion_vincular_cierre
            BEFORE INSERT OR UPDATE OR DELETE ON caja.autorizacion_cierre_turno
            FOR EACH ROW EXECUTE FUNCTION caja.vincular_autorizacion_cierre();

            CREATE FUNCTION caja.proteger_autorizacion_consumida()
            RETURNS trigger LANGUAGE plpgsql AS $funcion$
            BEGIN
                IF EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno WHERE autorizacion_id = OLD.id) THEN
                    RAISE EXCEPTION 'La autorizacion consumida por un cierre es historica e inmutable.';
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END;
            $funcion$;

            CREATE TRIGGER tg_autorizacion_proteger_consumida
            BEFORE UPDATE OR DELETE ON seguridad.autorizacion_operacion
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_autorizacion_consumida();

            CREATE FUNCTION caja.proteger_contexto_cierre()
            RETURNS trigger LANGUAGE plpgsql AS $funcion$
            BEGIN
                IF TG_TABLE_NAME = 'instalacion' THEN
                    IF NEW.establecimiento_id IS DISTINCT FROM OLD.establecimiento_id
                       AND EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno WHERE instalacion_id = OLD.id) THEN
                        RAISE EXCEPTION 'No se puede reasignar el establecimiento de una instalacion con cierres autorizados.';
                    END IF;
                ELSIF TG_TABLE_NAME = 'caja' THEN
                    IF NEW.instalacion_id IS DISTINCT FROM OLD.instalacion_id
                       AND EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno l
                                   JOIN caja.turno_caja t ON t.id = l.turno_caja_id WHERE t.caja_id = OLD.id) THEN
                        RAISE EXCEPTION 'No se puede reasignar la instalacion de una caja con cierres autorizados.';
                    END IF;
                ELSIF TG_TABLE_NAME = 'terminal' THEN
                    IF NEW.instalacion_id IS DISTINCT FROM OLD.instalacion_id
                       AND EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno l
                                   JOIN caja.turno_caja t ON t.id = l.turno_caja_id WHERE t.terminal_id = OLD.id) THEN
                        RAISE EXCEPTION 'No se puede reasignar la instalacion de una terminal con cierres autorizados.';
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $funcion$;

            CREATE TRIGGER tg_instalacion_proteger_contexto_cierre
            BEFORE UPDATE OF establecimiento_id ON configuracion.instalacion
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_contexto_cierre();
            CREATE TRIGGER tg_caja_proteger_contexto_cierre
            BEFORE UPDATE OF instalacion_id ON caja.caja
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_contexto_cierre();
            CREATE TRIGGER tg_terminal_proteger_contexto_cierre
            BEFORE UPDATE OF instalacion_id ON configuracion.terminal
            FOR EACH ROW EXECUTE FUNCTION caja.proteger_contexto_cierre();

            CREATE FUNCTION caja.validar_integridad_arqueo(p_turno_caja_id uuid)
            RETURNS void
            LANGUAGE plpgsql
            AS $funcion$
            DECLARE
                v_turno caja.turno_caja%ROWTYPE;
                v_cantidad_detalles bigint;
                v_efectivo_esperado numeric;
                v_efectivo_contado numeric;
                v_total_esperado numeric;
                v_total_contado numeric;
                v_diferencia_general numeric;
            BEGIN
                SELECT * INTO v_turno
                FROM caja.turno_caja
                WHERE id = p_turno_caja_id;

                IF NOT FOUND THEN RETURN; END IF;
                IF v_turno.estado <> 'CERRADA' THEN
                    IF EXISTS (SELECT 1 FROM caja.detalle_arqueo_medio_pago WHERE turno_caja_id = p_turno_caja_id)
                       OR EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno WHERE turno_caja_id = p_turno_caja_id) THEN
                        RAISE EXCEPTION 'El detalle y la autorizacion requieren completar el cierre en la misma transaccion.';
                    END IF;
                    RETURN;
                END IF;

                SELECT count(*),
                       COALESCE(sum(d.valor_esperado) FILTER (WHERE m.afecta_efectivo), 0),
                       COALESCE(sum(d.valor_contado) FILTER (WHERE m.afecta_efectivo), 0),
                       COALESCE(sum(d.diferencia), 0),
                       COALESCE(sum(d.valor_esperado), 0),
                       COALESCE(sum(d.valor_contado), 0)
                  INTO v_cantidad_detalles, v_efectivo_esperado,
                       v_efectivo_contado, v_diferencia_general, v_total_esperado, v_total_contado
                FROM caja.detalle_arqueo_medio_pago d
                JOIN catalogo.metodo_pago m ON m.id = d.metodo_pago_id
                WHERE d.turno_caja_id = p_turno_caja_id;

                IF v_cantidad_detalles = 0 THEN
                    RAISE EXCEPTION 'El cierre requiere detalle de arqueo por medio de pago.';
                END IF;

                IF v_turno.efectivo_esperado IS DISTINCT FROM v_efectivo_esperado
                   OR v_turno.efectivo_contado IS DISTINCT FROM v_efectivo_contado
                   OR v_turno.diferencia IS DISTINCT FROM (v_efectivo_contado - v_efectivo_esperado)
                   OR v_turno.diferencia_total IS DISTINCT FROM v_diferencia_general
                   OR v_diferencia_general <> v_total_contado - v_total_esperado
                   OR abs(v_total_esperado) > 999999999999999999
                   OR abs(v_total_contado) > 999999999999999999
                THEN
                    RAISE EXCEPTION 'Los totales del turno no coinciden con el detalle del arqueo.';
                END IF;

                IF v_turno.diferencia_total = 0 AND EXISTS (
                    SELECT 1 FROM caja.autorizacion_cierre_turno WHERE turno_caja_id = p_turno_caja_id
                ) THEN
                    RAISE EXCEPTION 'Un cierre sin diferencia total no debe consumir autorizacion adicional.';
                END IF;

                IF v_turno.diferencia_total <> 0 AND NOT EXISTS (
                    SELECT 1
                    FROM caja.autorizacion_cierre_turno l
                    JOIN seguridad.autorizacion_operacion a ON a.id = l.autorizacion_id
                    JOIN caja.caja c ON c.id = v_turno.caja_id
                    JOIN configuracion.instalacion i ON i.id = c.instalacion_id
                    JOIN configuracion.terminal t ON t.id = v_turno.terminal_id
                    WHERE l.turno_caja_id = p_turno_caja_id
                      AND l.usuario_ejecutor_id = v_turno.usuario_cierre_id
                      AND l.correlacion_id = a.correlacion_id
                      AND l.instalacion_id = i.id AND l.establecimiento_id = i.establecimiento_id
                      AND t.instalacion_id = i.id
                      AND a.usuario_solicitante_id = v_turno.usuario_cierre_id
                      AND a.usuario_autorizador_id <> v_turno.usuario_cierre_id
                      AND a.tipo_operacion = 'CIERRE_CAJA_DIFERENCIA'
                      AND a.estado = 'UTILIZADA'
                      AND a.resultado = 'EXITOSA'
                      AND a.fecha_utilizacion = l.fecha_vinculacion
                      AND a.fecha_solicitud <= l.fecha_vinculacion
                      AND a.fecha_expiracion > l.fecha_vinculacion
                ) THEN
                    RAISE EXCEPTION 'La autorizacion del cierre con diferencia no es valida.';
                END IF;
            END;
            $funcion$;

            CREATE FUNCTION caja.validar_arqueo_desde_turno()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $funcion$
            BEGIN
                PERFORM caja.validar_integridad_arqueo(NEW.id);
                RETURN NEW;
            END;
            $funcion$;

            CREATE FUNCTION caja.validar_arqueo_desde_detalle()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $funcion$
            BEGIN
                IF TG_OP IN ('UPDATE', 'DELETE') THEN
                    PERFORM caja.validar_integridad_arqueo(OLD.turno_caja_id);
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                PERFORM caja.validar_integridad_arqueo(NEW.turno_caja_id);
                RETURN NEW;
            END;
            $funcion$;

            CREATE CONSTRAINT TRIGGER tg_turno_validar_integridad_arqueo
            AFTER INSERT OR UPDATE ON caja.turno_caja
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION caja.validar_arqueo_desde_turno();

            CREATE CONSTRAINT TRIGGER tg_detalle_validar_integridad_arqueo
            AFTER INSERT OR UPDATE OR DELETE
            ON caja.detalle_arqueo_medio_pago
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION caja.validar_arqueo_desde_detalle();

            CREATE CONSTRAINT TRIGGER tg_autorizacion_validar_integridad_cierre
            AFTER INSERT OR UPDATE OR DELETE ON caja.autorizacion_cierre_turno
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION caja.validar_arqueo_desde_detalle();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            LOCK TABLE caja.turno_caja, caja.detalle_arqueo_medio_pago,
                caja.autorizacion_cierre_turno, ventas.pago_venta, ventas.pago_credito,
                ventas.pago_apartado, ventas.pago_cambio_venta, ventas.reembolso_apartado
                IN ACCESS EXCLUSIVE MODE;
            DO $precondicion$
            BEGIN
                IF EXISTS (SELECT 1 FROM caja.detalle_arqueo_medio_pago)
                   OR EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno)
                   OR EXISTS (SELECT 1 FROM caja.turno_caja WHERE estado = 'CERRADA') THEN
                    RAISE EXCEPTION 'No se puede revertir Caja con arqueos o cierres historicos; se necesita una migracion de transicion especifica.';
                END IF;
                IF EXISTS (SELECT movimiento_caja_id FROM ventas.pago_venta
                           WHERE movimiento_caja_id IS NOT NULL GROUP BY movimiento_caja_id HAVING count(*) > 1)
                   OR EXISTS (SELECT movimiento_caja_id FROM ventas.pago_credito
                              WHERE movimiento_caja_id IS NOT NULL GROUP BY movimiento_caja_id HAVING count(*) > 1)
                   OR EXISTS (SELECT movimiento_caja_id FROM ventas.pago_apartado
                              WHERE movimiento_caja_id IS NOT NULL GROUP BY movimiento_caja_id HAVING count(*) > 1)
                   OR EXISTS (SELECT movimiento_caja_id FROM ventas.pago_cambio_venta
                              WHERE movimiento_caja_id IS NOT NULL GROUP BY movimiento_caja_id HAVING count(*) > 1)
                   OR EXISTS (SELECT movimiento_caja_id FROM ventas.reembolso_apartado
                              WHERE movimiento_caja_id IS NOT NULL GROUP BY movimiento_caja_id HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'No se puede revertir Caja con pagos combinados que comparten movimiento; se necesita una migracion de transicion especifica.';
                END IF;
            END;
            $precondicion$;

            DROP TRIGGER tg_autorizacion_validar_integridad_cierre ON caja.autorizacion_cierre_turno;
            DROP TRIGGER tg_detalle_validar_integridad_arqueo ON caja.detalle_arqueo_medio_pago;
            DROP TRIGGER tg_turno_validar_integridad_arqueo ON caja.turno_caja;
            DROP TRIGGER tg_detalle_proteger_arqueo ON caja.detalle_arqueo_medio_pago;
            DROP TRIGGER tg_turno_proteger_cierre ON caja.turno_caja;
            DROP TRIGGER tg_metodo_proteger_arqueo ON catalogo.metodo_pago;
            DROP TRIGGER tg_autorizacion_vincular_cierre ON caja.autorizacion_cierre_turno;
            DROP TRIGGER tg_autorizacion_proteger_consumida ON seguridad.autorizacion_operacion;
            DROP TRIGGER tg_instalacion_proteger_contexto_cierre ON configuracion.instalacion;
            DROP TRIGGER tg_caja_proteger_contexto_cierre ON caja.caja;
            DROP TRIGGER tg_terminal_proteger_contexto_cierre ON configuracion.terminal;
            DROP FUNCTION caja.validar_arqueo_desde_detalle();
            DROP FUNCTION caja.validar_arqueo_desde_turno();
            DROP FUNCTION caja.validar_integridad_arqueo(uuid);
            DROP FUNCTION caja.proteger_detalle_arqueo();
            DROP FUNCTION caja.proteger_turno_cerrado();
            DROP FUNCTION caja.proteger_metodo_arqueado();
            DROP FUNCTION caja.vincular_autorizacion_cierre();
            DROP FUNCTION caja.proteger_autorizacion_consumida();
            DROP FUNCTION caja.proteger_contexto_cierre();
            DROP TABLE caja.detalle_arqueo_medio_pago;
            DROP TABLE caja.autorizacion_cierre_turno;
            -- Catalog entries intentionally survive rollback: provenance is unknown,
            -- and existing names and references must be preserved without modification.
            ALTER TABLE caja.turno_caja
                DROP CONSTRAINT ck_turno_caja_diferencia_total,
                DROP CONSTRAINT ck_turno_caja_motivo_diferencia,
                DROP COLUMN diferencia_total,
                ADD CONSTRAINT ck_turno_caja_motivo_diferencia CHECK (
                    diferencia IS NULL
                    OR (diferencia = 0 AND motivo_diferencia_id IS NULL)
                    OR (diferencia <> 0 AND motivo_diferencia_id IS NOT NULL)
                );

            DROP INDEX ventas.ix_pago_venta_movimiento_caja;
            DROP INDEX ventas.ix_pago_credito_movimiento_caja;
            DROP INDEX ventas.ix_pago_apartado_movimiento_caja;
            DROP INDEX ventas.ix_pago_cambio_movimiento_caja;
            DROP INDEX ventas.ix_reembolso_apartado_movimiento;
            ALTER TABLE ventas.pago_venta ADD CONSTRAINT uq_pago_venta_movimiento_caja UNIQUE (movimiento_caja_id);
            ALTER TABLE ventas.pago_credito ADD CONSTRAINT uq_pago_credito_movimiento_caja UNIQUE (movimiento_caja_id);
            ALTER TABLE ventas.pago_apartado ADD CONSTRAINT uq_pago_apartado_movimiento_caja UNIQUE (movimiento_caja_id);
            ALTER TABLE ventas.pago_cambio_venta ADD CONSTRAINT uq_pago_cambio_movimiento_caja UNIQUE (movimiento_caja_id);
            ALTER TABLE ventas.reembolso_apartado ADD CONSTRAINT uq_reembolso_apartado_movimiento UNIQUE (movimiento_caja_id);
            """);
    }
}
