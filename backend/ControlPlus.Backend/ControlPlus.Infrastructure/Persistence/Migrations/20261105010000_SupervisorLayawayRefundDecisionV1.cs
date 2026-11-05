using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261105010000_SupervisorLayawayRefundDecisionV1")]
public sealed class SupervisorLayawayRefundDecisionV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $guard$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM seguridad.rol WHERE codigo = 'SUPERVISOR' AND activo)
               OR NOT EXISTS (SELECT 1 FROM seguridad.permiso WHERE codigo = 'LAYAWAY.REFUND_DECIDE' AND activo)
            THEN RAISE EXCEPTION 'Falta el rol o permiso activo de decisión de devolución de apartados.';
            END IF;
        END $guard$;

        INSERT INTO seguridad.rol_permiso (rol_id, permiso_id)
        SELECT r.id, p.id FROM seguridad.rol r CROSS JOIN seguridad.permiso p
        WHERE r.codigo = 'SUPERVISOR' AND p.codigo = 'LAYAWAY.REFUND_DECIDE'
        ON CONFLICT DO NOTHING;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DO $guard$ BEGIN RAISE EXCEPTION 'La concesión de Supervisor requiere una reversión compensatoria revisada'; END $guard$;");
}
