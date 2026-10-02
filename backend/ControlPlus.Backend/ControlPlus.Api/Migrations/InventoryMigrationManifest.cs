namespace ControlPlus.Api.Migrations;

public static class InventoryMigrationManifest
{
    public const string TargetMigration = "20260930010000_SeedInventoryAdjustmentReasonsV1";
    public const string OperationType = "AJUSTE_INVENTARIO";
    public const string Schema = "configuracion";
    public const string Table = "motivo_operacion";
    public const string NaturalKeyConstraint = "uq_motivo_operacion_tipo_codigo";
    public const string DeterministicIdPrefix = "controlplus.inventory-reason.";

    public static IReadOnlyList<string> RequiredPreviousMigrations { get; } = Array.AsReadOnly<string>(
    [
        "20260912010000_OfficialPhase4Baseline",
        "20260912011000_SeedApprovedSecurityCatalog",
        "20260912012000_HybridPermissionsV1",
        "20260913010000_SeedMeasurementUnitsV1",
        CashRegisterPreflightManifest.TargetMigration
    ]);

    public static IReadOnlyList<InventoryAdjustmentReasonDefinition> Reasons { get; } =
        Array.AsReadOnly<InventoryAdjustmentReasonDefinition>(
        [
            Reason("CONTEO_FISICO", "Diferencia encontrada al contar existencias.", 10),
            Reason("MERMA_DANO", "Producto da\u00f1ado o no vendible.", 20),
            Reason("PERDIDA", "P\u00e9rdida o faltante.", 30),
            Reason("CORRECCION_REGISTRO", "Correcci\u00f3n documentada de un registro previo.", 40)
        ]);

    public static IReadOnlyList<InventoryTableColumn> RequiredColumns { get; } =
        Array.AsReadOnly<InventoryTableColumn>(
        [
            new("id", "uuid", null, IsNullable: false),
            new("establecimiento_id", "uuid", null, IsNullable: false),
            new("tipo_operacion", "character varying", 40, IsNullable: false),
            new("codigo", "character varying", 50, IsNullable: false),
            new("nombre", "character varying", 150, IsNullable: false),
            new("descripcion", "character varying", 500, IsNullable: true),
            new("orden_visual", "integer", null, IsNullable: false),
            new("activo", "boolean", null, IsNullable: false),
            new("fecha_creacion", "timestamp with time zone", null, IsNullable: false),
            new("fecha_modificacion", "timestamp with time zone", null, IsNullable: true),
            new("version", "bigint", null, IsNullable: false)
        ]);

    public static PreflightRequiredConstraint RequiredNaturalKeyConstraint { get; } = new(
        Schema,
        Table,
        NaturalKeyConstraint,
        PreflightConstraintKind.Unique,
        ["establecimiento_id", "tipo_operacion", "codigo"],
        MustBeValidated: true);

    private static InventoryAdjustmentReasonDefinition Reason(
        string code,
        string description,
        int displayOrder) =>
        new(code, displayOrder, description, description);
}

public sealed record InventoryAdjustmentReasonDefinition(
    string Code,
    int DisplayOrder,
    string Name,
    string Description);

public sealed record InventoryTableColumn(
    string Name,
    string DataType,
    int? CharacterMaximumLength,
    bool IsNullable);
