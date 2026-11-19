using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261119040000_CashInventoryCorrectionsV1")]
public sealed class CashInventoryCorrectionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE compras.correccion_movimiento_inventario (
          id uuid PRIMARY KEY,
          movimiento_original_id uuid NOT NULL UNIQUE REFERENCES compras.movimiento_inventario(id) ON DELETE RESTRICT,
          movimiento_corrector_id uuid NOT NULL UNIQUE REFERENCES compras.movimiento_inventario(id) ON DELETE RESTRICT,
          fecha_hora timestamptz NOT NULL,
          CONSTRAINT ck_correccion_inventario_distintos CHECK(movimiento_original_id <> movimiento_corrector_id)
        );
        CREATE TABLE caja.revision_cierre (
          id uuid PRIMARY KEY, turno_caja_id uuid NOT NULL REFERENCES caja.turno_caja(id) ON DELETE RESTRICT,
          usuario_id uuid NOT NULL REFERENCES seguridad.usuario(id) ON DELETE RESTRICT,
          efectivo_esperado numeric(18,0) NOT NULL CHECK(efectivo_esperado >= 0),
          contado_original numeric(18,0) NOT NULL CHECK(contado_original >= 0),
          contado_revisado numeric(18,0) NOT NULL CHECK(contado_revisado >= 0),
          motivo varchar(500) NOT NULL CHECK(btrim(motivo) <> ''),
          fecha_hora timestamptz NOT NULL, correlacion_id uuid NOT NULL
        );
        CREATE INDEX ix_revision_cierre_turno_fecha ON caja.revision_cierre(turno_caja_id, fecha_hora);
        CREATE FUNCTION caja.proteger_revision_cierre() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
          IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'Una revisión es histórica e inmutable.'; END IF;
          IF NOT EXISTS (SELECT 1 FROM caja.turno_caja t WHERE t.id=NEW.turno_caja_id AND t.estado='CERRADA'
            AND t.efectivo_esperado=NEW.efectivo_esperado AND t.efectivo_contado=NEW.contado_original)
          THEN RAISE EXCEPTION 'La revisión debe conservar el cierre original.'; END IF;
          RETURN NEW;
        END; $fn$;
        CREATE TRIGGER tg_revision_cierre_integridad BEFORE INSERT OR UPDATE OR DELETE ON caja.revision_cierre
          FOR EACH ROW EXECUTE FUNCTION caja.proteger_revision_cierre();
        CREATE FUNCTION compras.validar_correccion_ajuste() RETURNS trigger LANGUAGE plpgsql AS $fn$
        DECLARE o compras.movimiento_inventario%ROWTYPE; n compras.movimiento_inventario%ROWTYPE;
        BEGIN
          IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'El vínculo de corrección es inmutable.'; END IF;
          SELECT * INTO o FROM compras.movimiento_inventario WHERE id=NEW.movimiento_original_id;
          SELECT * INTO n FROM compras.movimiento_inventario WHERE id=NEW.movimiento_corrector_id;
          IF o.tipo_movimiento NOT IN ('AJUSTE_ENTRADA','AJUSTE_SALIDA')
            OR n.tipo_movimiento NOT IN ('AJUSTE_ENTRADA','AJUSTE_SALIDA') OR o.producto_id<>n.producto_id
            OR o.cantidad_stock::bigint+n.cantidad_stock::bigint<>0
          THEN RAISE EXCEPTION 'Solo puede compensarse íntegramente un ajuste manual del mismo producto.'; END IF;
          RETURN NEW;
        END; $fn$;
        CREATE TRIGGER tg_correccion_ajuste_integridad BEFORE INSERT OR UPDATE OR DELETE ON compras.correccion_movimiento_inventario
          FOR EACH ROW EXECUTE FUNCTION compras.validar_correccion_ajuste();
        DO $fn$ BEGIN IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='controlplus_app') THEN
          GRANT SELECT, INSERT ON compras.correccion_movimiento_inventario, caja.revision_cierre TO controlplus_app;
        END IF; END; $fn$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Las correcciones históricas no admiten Down automático.");
}
