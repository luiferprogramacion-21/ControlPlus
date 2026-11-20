using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>Approved receipt correction: the original receipt stays immutable and a certified reversal restores the pending order.</summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261120040000_PurchaseReceiptCorrectionsV1")]
public sealed class PurchaseReceiptCorrectionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE compras.correccion_recepcion (
              id uuid CONSTRAINT pk_correccion_recepcion PRIMARY KEY,
              compra_id uuid NOT NULL UNIQUE REFERENCES compras.compra(id) ON DELETE RESTRICT,
              pedido_compra_id uuid NOT NULL REFERENCES compras.pedido_compra(id) ON DELETE RESTRICT,
              usuario_id uuid NOT NULL REFERENCES seguridad.usuario(id) ON DELETE RESTRICT,
              autorizacion_operacion_id uuid NOT NULL REFERENCES seguridad.autorizacion_operacion(id) ON DELETE RESTRICT,
              version_pedido_anterior bigint NOT NULL CHECK (version_pedido_anterior > 0),
              version_pedido_posterior bigint NOT NULL,
              estado_anterior varchar(30) NOT NULL CHECK (estado_anterior IN ('ENVIADO','RECIBIDO_PARCIAL','COMPLETADO')),
              estado_posterior varchar(30) NOT NULL CHECK (estado_posterior IN ('ENVIADO','RECIBIDO_PARCIAL','COMPLETADO')),
              fecha_hora timestamptz NOT NULL,
              correlacion_id uuid NOT NULL,
              CONSTRAINT ck_correccion_recepcion_version CHECK (version_pedido_posterior = version_pedido_anterior + 1)
            );
            CREATE FUNCTION compras.proteger_correccion_recepcion() RETURNS trigger LANGUAGE plpgsql AS $correction$
            BEGIN
              IF TG_OP <> 'INSERT' THEN RAISE EXCEPTION 'La correccion de recepcion es inmutable.'; END IF;
              IF NOT EXISTS (SELECT 1 FROM compras.compra c JOIN compras.pedido_compra p ON p.id = c.pedido_compra_id
                 JOIN seguridad.autorizacion_operacion a ON a.id = NEW.autorizacion_operacion_id
                 WHERE c.id = NEW.compra_id AND p.id = NEW.pedido_compra_id
                   AND c.estado = 'ANULADA' AND c.autorizacion_anulacion_id = a.id
                   AND p.version = NEW.version_pedido_anterior AND p.estado = NEW.estado_anterior
                   AND a.usuario_solicitante_id = NEW.usuario_id AND a.estado = 'UTILIZADA'
                   AND a.tipo_operacion = 'ANULAR_COMPRA') THEN
                RAISE EXCEPTION 'La correccion no acredita recepcion anulada y autorizacion.';
              END IF;
              RETURN NEW;
            END; $correction$;
            CREATE TRIGGER tg_correccion_recepcion_proteger BEFORE INSERT OR UPDATE OR DELETE ON compras.correccion_recepcion
              FOR EACH ROW EXECUTE FUNCTION compras.proteger_correccion_recepcion();
            DO $grant$ BEGIN IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
              GRANT SELECT, INSERT ON compras.correccion_recepcion TO controlplus_app;
            END IF; END; $grant$;
            """);
        var progress = OfficialFunction("compras.validar_avance_pedido()");
        const string selection = "WHERE c.estado IN ('CONFIRMADA', 'ANULADA')";
        if (!progress.Contains(selection, StringComparison.Ordinal)) throw new InvalidOperationException("Unrecognized order validator.");
        // Preserve historical annulled receipts; exclude only the ones with certified, linked correction evidence.
        migrationBuilder.Sql(progress.Replace(selection,
            "WHERE (c.estado = 'CONFIRMADA' OR (c.estado = 'ANULADA' AND NOT EXISTS (SELECT 1 FROM compras.correccion_recepcion cr WHERE cr.compra_id = c.id)))", StringComparison.Ordinal));
        var protection = OfficialFunction("compras.proteger_pedido_compra()");
        const string guard = "IF NEW.estado IS DISTINCT FROM OLD.estado\n       AND NOT (";
        if (!protection.Contains(guard, StringComparison.Ordinal)) throw new InvalidOperationException("Unrecognized order state guard.");
        // All original transitions/checks remain; exception requires exact old/new versions and states in append-only evidence.
        migrationBuilder.Sql(protection.Replace(guard, """
            IF NEW.estado IS DISTINCT FROM OLD.estado
               AND NOT EXISTS (SELECT 1 FROM compras.correccion_recepcion cr
                 WHERE cr.pedido_compra_id = NEW.id AND cr.version_pedido_anterior = OLD.version
                   AND cr.version_pedido_posterior = NEW.version AND cr.estado_anterior = OLD.estado
                   AND cr.estado_posterior = NEW.estado)
               AND NOT (
            """, StringComparison.Ordinal));
    }

    private static string OfficialFunction(string name)
    {
        var sql = OfficialDatabaseScript.Load().Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = sql.IndexOf("CREATE FUNCTION " + name, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Official function not found.");
        var body = sql.IndexOf("AS $funcion$", start, StringComparison.Ordinal);
        var end = sql.IndexOf("$funcion$;", body + "AS $funcion$".Length, StringComparison.Ordinal);
        if (body < 0 || end < 0) throw new InvalidOperationException("Official function boundary not found.");
        return sql[start..(end + "$funcion$;".Length)].Replace("CREATE FUNCTION ", "CREATE OR REPLACE FUNCTION ", StringComparison.Ordinal);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("La correccion de recepciones conserva historial; requiere una migracion compensatoria.");
}
