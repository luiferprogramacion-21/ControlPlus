using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261120020000_LayawayCorrectionsV1")]
public sealed class LayawayCorrectionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE ventas.apartado ADD COLUMN total_original numeric(18,0);
        UPDATE ventas.apartado SET total_original = total;
        ALTER TABLE ventas.apartado ALTER COLUMN total_original SET NOT NULL;
        ALTER TABLE ventas.apartado DROP CONSTRAINT ck_apartado_montos;
        ALTER TABLE ventas.apartado ADD CONSTRAINT ck_apartado_montos CHECK (
          subtotal_bruto >= 0 AND descuento_total >= 0 AND descuento_total <= subtotal_bruto
          AND total > 0 AND total_original > 0 AND total = subtotal_bruto - descuento_total
          AND porcentaje_minimo_inicial BETWEEN 0 AND 100
          AND abono_inicial >= CASE WHEN porcentaje_minimo_inicial=0 THEN 1
              ELSE ceil(total_original * porcentaje_minimo_inicial / 100) END
          AND abono_inicial <= total_original AND saldo_pendiente BETWEEN 0 AND total);

        CREATE TABLE ventas.correccion_apartado (
          id uuid PRIMARY KEY, apartado_id uuid NOT NULL REFERENCES ventas.apartado(id) ON DELETE RESTRICT,
          usuario_id uuid NOT NULL REFERENCES seguridad.usuario(id) ON DELETE RESTRICT,
          autorizacion_id uuid NOT NULL REFERENCES seguridad.autorizacion_operacion(id) ON DELETE RESTRICT,
          version_anterior bigint NOT NULL, version_posterior bigint NOT NULL,
          total_anterior numeric(18,0) NOT NULL, total_posterior numeric(18,0) NOT NULL,
          saldo_anterior numeric(18,0) NOT NULL, saldo_posterior numeric(18,0) NOT NULL,
          reembolso numeric(18,0) NOT NULL, motivo varchar(500) NOT NULL,
          antes jsonb NOT NULL, despues jsonb NOT NULL,
          fecha_hora timestamptz NOT NULL, correlacion_id uuid NOT NULL UNIQUE,
          CONSTRAINT uq_correccion_apartado_version UNIQUE(apartado_id,version_posterior),
          CONSTRAINT ck_correccion_apartado_version CHECK(version_anterior>0 AND version_posterior=version_anterior+1),
          CONSTRAINT ck_correccion_apartado_montos CHECK(total_anterior>0 AND total_posterior>0
            AND saldo_anterior BETWEEN 0 AND total_anterior AND saldo_posterior BETWEEN 0 AND total_posterior
            AND reembolso>=0 AND reembolso=greatest(0,total_anterior-saldo_anterior-total_posterior)
            AND saldo_posterior=greatest(0,total_posterior-(total_anterior-saldo_anterior))),
          CONSTRAINT ck_correccion_apartado_snapshot CHECK(jsonb_typeof(antes)='array' AND jsonb_typeof(despues)='array'
            AND jsonb_array_length(despues)>0 AND btrim(motivo)<>'')
        );
        CREATE TABLE ventas.reembolso_correccion_apartado (
          id uuid PRIMARY KEY, correccion_id uuid NOT NULL REFERENCES ventas.correccion_apartado(id) ON DELETE RESTRICT,
          turno_caja_id uuid NOT NULL REFERENCES caja.turno_caja(id) ON DELETE RESTRICT,
          metodo_pago_id uuid NOT NULL REFERENCES catalogo.metodo_pago(id) ON DELETE RESTRICT,
          movimiento_caja_id uuid NOT NULL REFERENCES caja.movimiento_caja(id) ON DELETE RESTRICT,
          valor numeric(18,0) NOT NULL CHECK(valor>0), referencia varchar(200),
          CONSTRAINT uq_reembolso_correccion_apartado_metodo UNIQUE(correccion_id,metodo_pago_id),
          CONSTRAINT ck_reembolso_correccion_apartado_referencia CHECK(referencia IS NULL OR btrim(referencia)<>'')
        );
        CREATE INDEX ix_reembolso_correccion_apartado_turno ON ventas.reembolso_correccion_apartado(turno_caja_id);
        CREATE INDEX ix_reembolso_correccion_apartado_movimiento ON ventas.reembolso_correccion_apartado(movimiento_caja_id);

        CREATE FUNCTION ventas.proteger_correccion_apartado() RETURNS trigger LANGUAGE plpgsql AS $f$
        BEGIN RAISE EXCEPTION 'Las correcciones de apartado son inmutables.' USING ERRCODE='23514'; END $f$;
        CREATE TRIGGER tg_correccion_apartado_inmutable BEFORE UPDATE OR DELETE ON ventas.correccion_apartado
          FOR EACH ROW EXECUTE FUNCTION ventas.proteger_correccion_apartado();
        CREATE TRIGGER tg_reembolso_correccion_apartado_inmutable BEFORE UPDATE OR DELETE ON ventas.reembolso_correccion_apartado
          FOR EACH ROW EXECUTE FUNCTION ventas.proteger_correccion_apartado();
        CREATE FUNCTION ventas.proteger_original_apartado() RETURNS trigger LANGUAGE plpgsql AS $f$
        BEGIN
          IF TG_OP='INSERT' THEN NEW.total_original := NEW.total;
          ELSIF NEW.total_original IS DISTINCT FROM OLD.total_original
              OR NEW.abono_inicial IS DISTINCT FROM OLD.abono_inicial
              OR NEW.porcentaje_minimo_inicial IS DISTINCT FROM OLD.porcentaje_minimo_inicial THEN
            RAISE EXCEPTION 'Los importes iniciales del apartado son historicos.' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $f$;
        CREATE TRIGGER tg_apartado_proteger_original BEFORE INSERT OR UPDATE ON ventas.apartado
          FOR EACH ROW EXECUTE FUNCTION ventas.proteger_original_apartado();

        -- A product correction keeps the replaced lines as history (vigente = false) and appends the new ones.
        ALTER TABLE ventas.detalle_apartado ADD COLUMN vigente boolean NOT NULL DEFAULT true;
        COMMENT ON COLUMN ventas.detalle_apartado.vigente IS
          'False cuando una correccion reemplazo la linea; los totales, la entrega y la cancelacion usan solo las vigentes.';

        -- Keep every existing totals check; change only the paid balance and initial-payment guards and count
        -- only the lines in force.
        DO $patch$
        DECLARE definition text; old_balance text := 'v_pagos <> v_cabecera.total - v_cabecera.saldo_pendiente';
          old_initial text := 'v_pagos < v_cabecera.abono_inicial';
          lines_pattern text := 'FROM ventas\.detalle_apartado\s+WHERE apartado_id = v_apartado_id;';
          patched text;
        BEGIN
          definition := pg_get_functiondef('ventas.validar_totales_apartado()'::regprocedure);
          IF strpos(definition,old_balance)=0 OR strpos(definition,old_initial)=0
            OR (SELECT count(*) FROM regexp_matches(definition,lines_pattern,'g'))<>1 THEN
            RAISE EXCEPTION 'El validador de apartados no coincide con el contrato esperado.';
          END IF;
          patched := regexp_replace(definition,lines_pattern,
            'FROM ventas.detalle_apartado WHERE apartado_id = v_apartado_id AND vigente;');
          IF patched = definition THEN
            RAISE EXCEPTION 'No se pudo limitar el validador de apartados a las lineas vigentes.';
          END IF;
          definition := patched;
          definition := replace(definition,old_balance,
            'v_pagos - COALESCE((SELECT sum(r.valor) FROM ventas.reembolso_correccion_apartado r JOIN ventas.correccion_apartado c ON c.id=r.correccion_id WHERE c.apartado_id=v_apartado_id),0) <> v_cabecera.total - v_cabecera.saldo_pendiente');
          definition := replace(definition,old_initial,
            'v_pagos < v_cabecera.abono_inicial - COALESCE((SELECT sum(p.valor_aplicado) FROM ventas.pago_apartado p JOIN caja.movimiento_caja m ON m.id=p.movimiento_caja_id WHERE p.apartado_id=v_apartado_id AND p.estado=''ANULADO'' AND m.categoria_movimiento=''APARTADO''),0)');
          definition := replace(definition,'v_cabecera.total_abonado_cancelacion <> v_pagos',
            'v_cabecera.total_abonado_cancelacion <> v_pagos - COALESCE((SELECT sum(r.valor) FROM ventas.reembolso_correccion_apartado r JOIN ventas.correccion_apartado c ON c.id=r.correccion_id WHERE c.apartado_id=v_apartado_id),0)');
          EXECUTE definition;
        END $patch$;

        CREATE FUNCTION ventas.validar_anulacion_pago_apartado() RETURNS trigger LANGUAGE plpgsql AS $f$
        DECLARE p ventas.pago_apartado%ROWTYPE; m caja.movimiento_caja%ROWTYPE; a seguridad.autorizacion_operacion%ROWTYPE;
          reversals integer; reversed numeric; applied integer;
        BEGIN
          SELECT * INTO p FROM ventas.pago_apartado WHERE id=NEW.id;
          IF p.estado <> 'ANULADO' THEN RETURN NEW; END IF;
          SELECT * INTO m FROM caja.movimiento_caja WHERE id=p.movimiento_caja_id;
          SELECT * INTO a FROM seguridad.autorizacion_operacion WHERE id=p.autorizacion_anulacion_id;
          SELECT count(*),coalesce(sum(valor),0) INTO reversals,reversed FROM caja.movimiento_caja
            WHERE movimiento_revertido_id=m.id AND tipo='REVERSO' AND categoria_movimiento='REVERSO_PAGO'
              AND correlacion_id=a.correlacion_id;
          SELECT count(*) INTO applied FROM ventas.pago_apartado WHERE movimiento_caja_id=m.id AND estado<>'ANULADO';
          IF m.id IS NULL OR m.tipo<>'INGRESO' OR m.categoria_movimiento NOT IN ('APARTADO','ABONO_APARTADO')
            OR reversals<>1 OR reversed<>m.valor OR applied<>0
            OR a.tipo_operacion<>'ANULACION_PAGO_APARTADO' OR a.estado<>'UTILIZADA' OR a.resultado<>'EXITOSA'
            OR a.usuario_solicitante_id<>p.usuario_anulacion_id
            OR NOT EXISTS(SELECT 1 FROM ventas.apartado x WHERE x.id=p.apartado_id AND x.estado IN ('ACTIVO','PAGADO')) THEN
            RAISE EXCEPTION 'La anulacion del abono requiere autorizacion, reverso completo y apartado sin entregar.' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $f$;
        CREATE CONSTRAINT TRIGGER tg_pago_apartado_validar_reverso AFTER UPDATE ON ventas.pago_apartado
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ventas.validar_anulacion_pago_apartado();

        CREATE FUNCTION ventas.validar_correccion_apartado() RETURNS trigger LANGUAGE plpgsql AS $f$
        DECLARE cid uuid; c ventas.correccion_apartado%ROWTYPE; refunded numeric;
        BEGIN
          cid := CASE WHEN TG_TABLE_NAME='correccion_apartado' THEN NEW.id ELSE NEW.correccion_id END;
          SELECT * INTO c FROM ventas.correccion_apartado WHERE id=cid;
          SELECT coalesce(sum(valor),0) INTO refunded FROM ventas.reembolso_correccion_apartado WHERE correccion_id=cid;
          IF refunded<>c.reembolso OR NOT EXISTS(SELECT 1 FROM seguridad.autorizacion_operacion a
            WHERE a.id=c.autorizacion_id AND a.usuario_solicitante_id=c.usuario_id
              AND a.tipo_operacion='CORRECCION_APARTADO' AND a.estado='UTILIZADA' AND a.resultado='EXITOSA') THEN
            RAISE EXCEPTION 'La correccion del apartado requiere autorizacion y reembolso exacto.' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $f$;
        CREATE CONSTRAINT TRIGGER tg_correccion_apartado_validar AFTER INSERT ON ventas.correccion_apartado
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ventas.validar_correccion_apartado();
        CREATE CONSTRAINT TRIGGER tg_reembolso_correccion_apartado_validar AFTER INSERT ON ventas.reembolso_correccion_apartado
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ventas.validar_correccion_apartado();
        DO $grants$ BEGIN
          IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='controlplus_app') THEN
            GRANT SELECT,INSERT ON ventas.correccion_apartado,ventas.reembolso_correccion_apartado TO controlplus_app;
            GRANT SELECT,INSERT,UPDATE ON ventas.detalle_apartado TO controlplus_app;
          END IF;
        END $grants$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN RAISE EXCEPTION 'Las correcciones historicas requieren migracion compensatoria revisada.'; END $guard$;
        """);
}
