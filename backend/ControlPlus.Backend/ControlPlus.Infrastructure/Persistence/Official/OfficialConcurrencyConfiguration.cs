using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

internal static class OfficialConcurrencyConfiguration
{
    private static readonly (string Schema, string Table)[] VersionedTables =
    [
        ("configuracion", "establecimiento"),
        ("configuracion", "instalacion"),
        ("configuracion", "terminal"),
        ("configuracion", "motivo_operacion"),
        ("seguridad", "usuario"),
        ("seguridad", "rol"),
        ("seguridad", "limite_operacion_rol"),
        ("seguridad", "credencial_usuario"),
        ("configuracion", "consecutivo_documento"),
        ("configuracion", "destino_respaldo"),
        ("configuracion", "perfil_impresora"),
        ("configuracion", "plantilla_impresion"),
        ("catalogo", "metodo_pago"),
        ("catalogo", "categoria"),
        ("catalogo", "producto"),
        ("catalogo", "cliente"),
        ("catalogo", "proveedor"),
        ("caja", "caja"),
        ("caja", "turno_caja"),
        ("ventas", "borrador_venta"),
        ("ventas", "detalle_borrador_venta"),
        ("ventas", "venta"),
        ("ventas", "credito"),
        ("ventas", "apartado"),
        ("compras", "pedido_compra"),
        ("compras", "detalle_pedido_compra"),
        ("compras", "compra")
    ];

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var (schema, table) in VersionedTables)
        {
            var entityType = modelBuilder.Model.GetEntityTypes().SingleOrDefault(candidate =>
                candidate.GetSchema() == schema && candidate.GetTableName() == table);
            if (entityType is null)
            {
                throw new InvalidOperationException($"The official entity mapped to {schema}.{table} was not found.");
            }

            var version = entityType.FindProperty("Version");
            if (version is null || version.ClrType != typeof(long))
            {
                throw new InvalidOperationException($"The official entity mapped to {schema}.{table} must expose a long Version property.");
            }

            version.IsConcurrencyToken = true;
        }
    }
}
