namespace ControlPlus.Api.Migrations;

public static class CustomerSearchMigrationManifest
{
    public const string TargetMigration = "20261005010000_CustomerSearchIndexesV1";
    public const string Schema = "catalogo";
    public const string Table = "cliente";

    public static IReadOnlyList<string> RequiredPreviousMigrations { get; } = Array.AsReadOnly<string>(
    [
        "20260912010000_OfficialPhase4Baseline",
        "20260912011000_SeedApprovedSecurityCatalog",
        "20260912012000_HybridPermissionsV1",
        "20260913010000_SeedMeasurementUnitsV1",
        CashRegisterPreflightManifest.TargetMigration,
        InventoryMigrationManifest.TargetMigration
    ]);

    public static IReadOnlyList<CustomerSearchColumnDefinition> RequiredColumns { get; } =
        Array.AsReadOnly<CustomerSearchColumnDefinition>(
        [
            new("id", "uuid", null, IsNullable: false),
            new("tipo_documento", "character varying", 20, IsNullable: true),
            new("numero_documento", "character varying", 50, IsNullable: true),
            new("nombre_completo", "character varying", 200, IsNullable: false),
            new("telefono", "character varying", 30, IsNullable: true),
            new("direccion", "character varying", 300, IsNullable: true),
            new("correo", "character varying", 254, IsNullable: true),
            new("observaciones", "text", null, IsNullable: true),
            new("activo", "boolean", null, IsNullable: false),
            new("usuario_creacion_id", "uuid", null, IsNullable: false),
            new("usuario_modificacion_id", "uuid", null, IsNullable: true),
            new("fecha_creacion", "timestamp with time zone", null, IsNullable: false),
            new("fecha_modificacion", "timestamp with time zone", null, IsNullable: true),
            new("version", "bigint", null, IsNullable: false)
        ]);

    public static IReadOnlyList<CustomerSearchIndexDefinition> Indexes { get; } =
        Array.AsReadOnly<CustomerSearchIndexDefinition>(
        [
            new(
                "ix_cliente_busqueda_nombre_prefijo",
                "CREATE INDEX ix_cliente_busqueda_nombre_prefijo ON catalogo.cliente USING btree (lower((nombre_completo)::text) text_pattern_ops)"),
            new(
                "ix_cliente_busqueda_documento_prefijo",
                "CREATE INDEX ix_cliente_busqueda_documento_prefijo ON catalogo.cliente USING btree (numero_documento varchar_pattern_ops) WHERE (numero_documento IS NOT NULL)"),
            new(
                "ix_cliente_busqueda_telefono_prefijo",
                "CREATE INDEX ix_cliente_busqueda_telefono_prefijo ON catalogo.cliente USING btree (lower((telefono)::text) text_pattern_ops) WHERE (telefono IS NOT NULL)"),
            new(
                "ix_cliente_activo_nombre_id",
                "CREATE INDEX ix_cliente_activo_nombre_id ON catalogo.cliente USING btree (activo, nombre_completo, id)")
        ]);
}

public sealed record CustomerSearchColumnDefinition(
    string Name,
    string DataType,
    int? CharacterMaximumLength,
    bool IsNullable);

public sealed record CustomerSearchIndexDefinition(string Name, string Definition);
