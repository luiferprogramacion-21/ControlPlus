using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261021010000_LayawayCancellationOptionalReasonV1")]
public sealed class LayawayCancellationOptionalReasonV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ReplaceConstraint(CurrentConstraint));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ReplaceConstraint(PreviousConstraint));

    private static string ReplaceConstraint(string constraint) =>
        "ALTER TABLE ventas.apartado DROP CONSTRAINT ck_apartado_transicion;\n" +
        "ALTER TABLE ventas.apartado ADD " + constraint + ";";

    // Preserve the delivered-without-sale rule from the preceding migration.
    private const string PreviousConstraint =
        """
            CONSTRAINT ck_apartado_transicion CHECK (
            (
            estado = 'ACTIVO'
            AND saldo_pendiente > 0
            AND fecha_pago IS NULL
            AND fecha_entrega IS NULL
            AND venta_entrega_id IS NULL
            AND usuario_entrega_id IS NULL
            AND fecha_anulacion IS NULL
            AND usuario_anulacion_id IS NULL
            AND autorizacion_anulacion_id IS NULL
            AND motivo_anulacion_id IS NULL
            AND observacion_anulacion IS NULL
            AND total_abonado_cancelacion IS NULL
            AND valor_devuelto_cancelacion IS NULL
            AND valor_retenido_cancelacion IS NULL
            )
            OR
            (
            estado = 'PAGADO'
            AND saldo_pendiente = 0
            AND fecha_pago IS NOT NULL
            AND fecha_entrega IS NULL
            AND venta_entrega_id IS NULL
            AND usuario_entrega_id IS NULL
            AND fecha_anulacion IS NULL
            AND usuario_anulacion_id IS NULL
            AND autorizacion_anulacion_id IS NULL
            AND motivo_anulacion_id IS NULL
            AND observacion_anulacion IS NULL
            AND total_abonado_cancelacion IS NULL
            AND valor_devuelto_cancelacion IS NULL
            AND valor_retenido_cancelacion IS NULL
            )
            OR
            (
            estado = 'ENTREGADO'
            AND saldo_pendiente = 0
            AND fecha_pago IS NOT NULL
            AND fecha_entrega IS NOT NULL

            AND usuario_entrega_id IS NOT NULL
            AND fecha_anulacion IS NULL
            AND usuario_anulacion_id IS NULL
            AND autorizacion_anulacion_id IS NULL
            AND motivo_anulacion_id IS NULL
            AND observacion_anulacion IS NULL
            AND total_abonado_cancelacion IS NULL
            AND valor_devuelto_cancelacion IS NULL
            AND valor_retenido_cancelacion IS NULL
            )
            OR
            (
            estado = 'ANULADO'
            AND fecha_anulacion IS NOT NULL
            AND usuario_anulacion_id IS NOT NULL
            AND autorizacion_anulacion_id IS NOT NULL
            AND motivo_anulacion_id IS NOT NULL
            AND total_abonado_cancelacion IS NOT NULL
            AND total_abonado_cancelacion >= 0
            AND valor_devuelto_cancelacion IS NOT NULL
            AND valor_devuelto_cancelacion >= 0
            AND valor_retenido_cancelacion IS NOT NULL
            AND valor_retenido_cancelacion >= 0
            AND valor_devuelto_cancelacion + valor_retenido_cancelacion
            = total_abonado_cancelacion
            AND (
            observacion_anulacion IS NULL
            OR btrim(observacion_anulacion) <> ''
            )
            )
            )
        """;

    // Only the cancellation reason changes from required to optional.
    private const string CurrentConstraint =
        """
            CONSTRAINT ck_apartado_transicion CHECK (
            (
            estado = 'ACTIVO'
            AND saldo_pendiente > 0
            AND fecha_pago IS NULL
            AND fecha_entrega IS NULL
            AND venta_entrega_id IS NULL
            AND usuario_entrega_id IS NULL
            AND fecha_anulacion IS NULL
            AND usuario_anulacion_id IS NULL
            AND autorizacion_anulacion_id IS NULL
            AND motivo_anulacion_id IS NULL
            AND observacion_anulacion IS NULL
            AND total_abonado_cancelacion IS NULL
            AND valor_devuelto_cancelacion IS NULL
            AND valor_retenido_cancelacion IS NULL
            )
            OR
            (
            estado = 'PAGADO'
            AND saldo_pendiente = 0
            AND fecha_pago IS NOT NULL
            AND fecha_entrega IS NULL
            AND venta_entrega_id IS NULL
            AND usuario_entrega_id IS NULL
            AND fecha_anulacion IS NULL
            AND usuario_anulacion_id IS NULL
            AND autorizacion_anulacion_id IS NULL
            AND motivo_anulacion_id IS NULL
            AND observacion_anulacion IS NULL
            AND total_abonado_cancelacion IS NULL
            AND valor_devuelto_cancelacion IS NULL
            AND valor_retenido_cancelacion IS NULL
            )
            OR
            (
            estado = 'ENTREGADO'
            AND saldo_pendiente = 0
            AND fecha_pago IS NOT NULL
            AND fecha_entrega IS NOT NULL

            AND usuario_entrega_id IS NOT NULL
            AND fecha_anulacion IS NULL
            AND usuario_anulacion_id IS NULL
            AND autorizacion_anulacion_id IS NULL
            AND motivo_anulacion_id IS NULL
            AND observacion_anulacion IS NULL
            AND total_abonado_cancelacion IS NULL
            AND valor_devuelto_cancelacion IS NULL
            AND valor_retenido_cancelacion IS NULL
            )
            OR
            (
            estado = 'ANULADO'
            AND fecha_anulacion IS NOT NULL
            AND usuario_anulacion_id IS NOT NULL
            AND autorizacion_anulacion_id IS NOT NULL

            AND total_abonado_cancelacion IS NOT NULL
            AND total_abonado_cancelacion >= 0
            AND valor_devuelto_cancelacion IS NOT NULL
            AND valor_devuelto_cancelacion >= 0
            AND valor_retenido_cancelacion IS NOT NULL
            AND valor_retenido_cancelacion >= 0
            AND valor_devuelto_cancelacion + valor_retenido_cancelacion
            = total_abonado_cancelacion
            AND (
            observacion_anulacion IS NULL
            OR btrim(observacion_anulacion) <> ''
            )
            )
            )
        """;
}
