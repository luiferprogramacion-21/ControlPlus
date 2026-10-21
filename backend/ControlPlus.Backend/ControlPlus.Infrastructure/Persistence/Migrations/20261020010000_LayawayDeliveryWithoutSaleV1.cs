using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ControlPlusDbContext))]
[Migration("20261020010000_LayawayDeliveryWithoutSaleV1")]
public sealed class LayawayDeliveryWithoutSaleV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ReplaceConstraint(OriginalConstraint.Replace(
            "AND venta_entrega_id IS NOT NULL", string.Empty, StringComparison.Ordinal)));

    // Restoring the original CHECK fails atomically if deliveries without a sale exist.
    // Never fabricate a sale or delete historical deliveries to make a downgrade pass.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ReplaceConstraint(OriginalConstraint));

    private static string ReplaceConstraint(string constraint) =>
        "ALTER TABLE ventas.apartado DROP CONSTRAINT ck_apartado_transicion;\n" +
        "ALTER TABLE ventas.apartado ADD " + constraint + ";";

    // Exact transition CHECK from the immutable Phase 4 baseline.
    private const string OriginalConstraint =
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
            AND venta_entrega_id IS NOT NULL
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
}
