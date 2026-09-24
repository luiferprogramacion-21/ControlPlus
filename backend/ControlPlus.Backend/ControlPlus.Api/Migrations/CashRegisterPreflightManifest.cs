namespace ControlPlus.Api.Migrations;

public static class CashRegisterPreflightManifest
{
    public const string TargetMigration = "20260913020000_CashRegisterModuleV1";

    public static IReadOnlyList<string> RequiredPreviousMigrations { get; } = Array.AsReadOnly<string>(
    [
        "20260912010000_OfficialPhase4Baseline",
        "20260912011000_SeedApprovedSecurityCatalog",
        "20260912012000_HybridPermissionsV1",
        "20260913010000_SeedMeasurementUnitsV1"
    ]);

    public static IReadOnlyList<PreflightTableObject> CreatedTables { get; } = Array.AsReadOnly<PreflightTableObject>(
    [
        new("caja", "autorizacion_cierre_turno"),
        new("caja", "detalle_arqueo_medio_pago")
    ]);

    public static PreflightColumnObject CreatedDifferenceTotalColumn { get; } =
        new("caja", "turno_caja", "diferencia_total");

    public static IReadOnlyList<PreflightIndexObject> CreatedIndexes { get; } = Array.AsReadOnly<PreflightIndexObject>(
    [
        new("caja", "ix_detalle_arqueo_metodo_pago", "detalle_arqueo_medio_pago", ["metodo_pago_id"]),
        new("ventas", "ix_pago_venta_movimiento_caja", "pago_venta", ["movimiento_caja_id"]),
        new("ventas", "ix_pago_credito_movimiento_caja", "pago_credito", ["movimiento_caja_id"]),
        new("ventas", "ix_pago_apartado_movimiento_caja", "pago_apartado", ["movimiento_caja_id"]),
        new("ventas", "ix_pago_cambio_movimiento_caja", "pago_cambio_venta", ["movimiento_caja_id"]),
        new("ventas", "ix_reembolso_apartado_movimiento", "reembolso_apartado", ["movimiento_caja_id"])
    ]);

    public static IReadOnlyList<PreflightFunctionObject> CreatedFunctions { get; } = Array.AsReadOnly<PreflightFunctionObject>(
    [
        new("caja", "proteger_detalle_arqueo", [], "trigger"),
        new("caja", "proteger_turno_cerrado", [], "trigger"),
        new("caja", "proteger_metodo_arqueado", [], "trigger"),
        new("caja", "vincular_autorizacion_cierre", [], "trigger"),
        new("caja", "proteger_autorizacion_consumida", [], "trigger"),
        new("caja", "proteger_contexto_cierre", [], "trigger"),
        new("caja", "validar_integridad_arqueo", ["uuid"], "void"),
        new("caja", "validar_arqueo_desde_turno", [], "trigger"),
        new("caja", "validar_arqueo_desde_detalle", [], "trigger")
    ]);

    public static IReadOnlyList<PreflightTriggerObject> CreatedTriggers { get; } = Array.AsReadOnly<PreflightTriggerObject>(
    [
        new("caja", "detalle_arqueo_medio_pago", "tg_detalle_proteger_arqueo"),
        new("caja", "turno_caja", "tg_turno_proteger_cierre"),
        new("catalogo", "metodo_pago", "tg_metodo_proteger_arqueo"),
        new("caja", "autorizacion_cierre_turno", "tg_autorizacion_vincular_cierre"),
        new("seguridad", "autorizacion_operacion", "tg_autorizacion_proteger_consumida"),
        new("configuracion", "instalacion", "tg_instalacion_proteger_contexto_cierre"),
        new("caja", "caja", "tg_caja_proteger_contexto_cierre"),
        new("configuracion", "terminal", "tg_terminal_proteger_contexto_cierre"),
        new("caja", "turno_caja", "tg_turno_validar_integridad_arqueo"),
        new("caja", "detalle_arqueo_medio_pago", "tg_detalle_validar_integridad_arqueo"),
        new("caja", "autorizacion_cierre_turno", "tg_autorizacion_validar_integridad_cierre")
    ]);

    public static IReadOnlyList<PreflightRequiredConstraint> RequiredBaseConstraints { get; } = Array.AsReadOnly<PreflightRequiredConstraint>(
    [
        new(
            "caja",
            "turno_caja",
            "ck_turno_caja_diferencia",
            PreflightConstraintKind.Check,
            ["diferencia", "efectivo_contado", "efectivo_esperado"],
            MustBeValidated: true),
        new(
            "caja",
            "turno_caja",
            "ck_turno_caja_motivo_diferencia",
            PreflightConstraintKind.Check,
            ["diferencia", "motivo_diferencia_id"],
            MustBeValidated: true),
        Unique("ventas", "pago_venta", "uq_pago_venta_movimiento_caja"),
        Unique("ventas", "pago_credito", "uq_pago_credito_movimiento_caja"),
        Unique("ventas", "pago_apartado", "uq_pago_apartado_movimiento_caja"),
        Unique("ventas", "pago_cambio_venta", "uq_pago_cambio_movimiento_caja"),
        Unique("ventas", "reembolso_apartado", "uq_reembolso_apartado_movimiento")
    ]);

    private static PreflightRequiredConstraint Unique(string schema, string table, string name) =>
        new(
            schema,
            table,
            name,
            PreflightConstraintKind.Unique,
            ["movimiento_caja_id"],
            MustBeValidated: true);
}

public sealed record PreflightTableObject(string Schema, string Name);

public sealed record PreflightColumnObject(string Schema, string Table, string Column);

public sealed record PreflightIndexObject(
    string Schema,
    string Name,
    string Table,
    IReadOnlyList<string> Columns);

public sealed record PreflightFunctionObject(
    string Schema,
    string Name,
    IReadOnlyList<string> ArgumentTypes,
    string ReturnType);

public sealed record PreflightTriggerObject(string Schema, string Table, string Name);

public sealed record PreflightRequiredConstraint(
    string Schema,
    string Table,
    string Name,
    PreflightConstraintKind Kind,
    IReadOnlyList<string> Columns,
    bool MustBeValidated);

public enum PreflightConstraintKind
{
    Check = 1,
    Unique = 2
}
