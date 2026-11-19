using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

/// <summary>
/// Configura el cajon conectado a una impresora de recibos y registra cada decision de apertura
/// antes de enviar el pulso. No reintenta pulsos de resultado incierto.
/// </summary>
[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261119010000_CashDrawerV1")]
public sealed class CashDrawerV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE configuracion.perfil_impresora
          ADD COLUMN cajon_habilitado boolean NOT NULL DEFAULT false,
          ADD COLUMN cajon_pin smallint NULL,
          ADD COLUMN cajon_pulso_on_ms smallint NOT NULL DEFAULT 100,
          ADD COLUMN cajon_pulso_off_ms smallint NOT NULL DEFAULT 100;

        ALTER TABLE configuracion.perfil_impresora
          ADD CONSTRAINT ck_perfil_impresora_cajon_pin
            CHECK (cajon_pin IS NULL OR cajon_pin IN (2, 5)),
          ADD CONSTRAINT ck_perfil_impresora_cajon_pulsos
            CHECK (cajon_pulso_on_ms BETWEEN 20 AND 500
              AND cajon_pulso_on_ms % 2 = 0
              AND cajon_pulso_off_ms BETWEEN 20 AND 500
              AND cajon_pulso_off_ms % 2 = 0),
          ADD CONSTRAINT ck_perfil_impresora_cajon_habilitado
            CHECK (NOT cajon_habilitado OR (
              activo AND tipo_impresora IN ('RECIBOS', 'HIBRIDA') AND cajon_pin IS NOT NULL));

        COMMENT ON COLUMN configuracion.perfil_impresora.cajon_habilitado IS
          'Desactivado hasta que un Administrador configure y pruebe el cajon conectado a esta impresora.';
        COMMENT ON COLUMN configuracion.perfil_impresora.cajon_pin IS
          'Salida ESC/POS pin 2 o 5, segun la impresora y el cable del cajon.';
        COMMENT ON COLUMN configuracion.perfil_impresora.cajon_pulso_on_ms IS
          'Duracion encendida del pulso ESC/POS en milisegundos, multiplo de 2.';
        COMMENT ON COLUMN configuracion.perfil_impresora.cajon_pulso_off_ms IS
          'Duracion apagada del pulso ESC/POS en milisegundos, multiplo de 2.';

        CREATE TABLE caja.apertura_cajon (
          id uuid NOT NULL,
          perfil_impresora_id uuid NULL,
          terminal_id uuid NOT NULL,
          turno_caja_id uuid NOT NULL,
          solicitado_por_id uuid NOT NULL,
          tipo_operacion varchar(30) NOT NULL,
          operacion_id uuid NOT NULL,
          estado varchar(20) NOT NULL DEFAULT 'INTENTANDO',
          motivo varchar(500) NULL,
          fecha_solicitud timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
          fecha_resultado timestamptz NULL,
          error_sanitizado varchar(300) NULL,
          CONSTRAINT pk_apertura_cajon PRIMARY KEY (id),
          CONSTRAINT fk_apertura_cajon_perfil FOREIGN KEY (perfil_impresora_id)
            REFERENCES configuracion.perfil_impresora(id) ON DELETE RESTRICT,
          CONSTRAINT fk_apertura_cajon_terminal FOREIGN KEY (terminal_id)
            REFERENCES configuracion.terminal(id) ON DELETE RESTRICT,
          CONSTRAINT fk_apertura_cajon_turno FOREIGN KEY (turno_caja_id)
            REFERENCES caja.turno_caja(id) ON DELETE RESTRICT,
          CONSTRAINT fk_apertura_cajon_usuario FOREIGN KEY (solicitado_por_id)
            REFERENCES seguridad.usuario(id) ON DELETE RESTRICT,
          CONSTRAINT ck_apertura_cajon_tipo CHECK (length(btrim(tipo_operacion)) > 0),
          CONSTRAINT ck_apertura_cajon_estado CHECK (
            estado IN ('INTENTANDO', 'ENVIADO', 'FALLIDO', 'INCIERTO', 'OMITIDO')),
          CONSTRAINT ck_apertura_cajon_perfil_estado CHECK (
            estado = 'OMITIDO' OR perfil_impresora_id IS NOT NULL),
          CONSTRAINT ck_apertura_cajon_motivo_manual CHECK (
            tipo_operacion <> 'MANUAL' OR (motivo IS NOT NULL AND btrim(motivo) <> '')),
          CONSTRAINT uq_apertura_cajon_operacion UNIQUE (tipo_operacion, operacion_id)
        );
        COMMENT ON TABLE caja.apertura_cajon IS
          'Decision durable por operacion: un replay no debe emitir un segundo pulso del cajon.';
        CREATE INDEX ix_apertura_cajon_terminal_fecha
          ON caja.apertura_cajon(terminal_id, fecha_solicitud DESC);
        CREATE INDEX ix_apertura_cajon_perfil
          ON caja.apertura_cajon(perfil_impresora_id);
        CREATE INDEX ix_apertura_cajon_turno
          ON caja.apertura_cajon(turno_caja_id);
        CREATE INDEX ix_apertura_cajon_usuario
          ON caja.apertura_cajon(solicitado_por_id);

        DO $grant$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app') THEN
            GRANT SELECT, INSERT, UPDATE, DELETE ON caja.apertura_cajon TO controlplus_app;
          END IF;
        END $grant$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE caja.apertura_cajon;
        ALTER TABLE configuracion.perfil_impresora
          DROP CONSTRAINT ck_perfil_impresora_cajon_habilitado,
          DROP CONSTRAINT ck_perfil_impresora_cajon_pulsos,
          DROP CONSTRAINT ck_perfil_impresora_cajon_pin;
        ALTER TABLE configuracion.perfil_impresora
          DROP COLUMN cajon_pulso_off_ms,
          DROP COLUMN cajon_pulso_on_ms,
          DROP COLUMN cajon_pin,
          DROP COLUMN cajon_habilitado;
        """);
}
