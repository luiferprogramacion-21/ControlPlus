namespace ControlPlus.Application.Security.Contracts;

public sealed record PermissionDefinition(string Code, string Name, string Module);

public static class DefaultPermissionCatalog
{
    public static readonly IReadOnlyCollection<PermissionDefinition> Permissions =
    [
        new(PermissionCodes.UsersRead, "Consultar usuarios", "SEGURIDAD"),
        new(PermissionCodes.UsersManage, "Gestionar usuarios", "SEGURIDAD"),
        new(PermissionCodes.CashierPasswordsReset, "Restablecer contraseñas de Cajeros", "SEGURIDAD"),
        new(PermissionCodes.RolesRead, "Consultar roles", "SEGURIDAD"),
        new(PermissionCodes.RolesManage, "Gestionar plantillas de roles", "SEGURIDAD"),
        new(PermissionCodes.PermissionsRead, "Consultar permisos", "SEGURIDAD"),
        new(PermissionCodes.PermissionsManage, "Gestionar permisos", "SEGURIDAD"),
        new(PermissionCodes.UserPermissionsManage, "Gestionar permisos individuales", "SEGURIDAD"),
        new(PermissionCodes.AuditRead, "Consultar auditoría", "AUDITORIA"),
        new(PermissionCodes.ProductsRead, "Consultar productos y existencias", "CATALOGO"),
        new(PermissionCodes.ProductsCreate, "Crear productos", "CATALOGO"),
        new(PermissionCodes.ProductsSensitiveUpdate, "Modificar datos sensibles de productos", "CATALOGO"),
        new(PermissionCodes.ProductCostsRead, "Consultar costos, utilidad y márgenes", "CATALOGO"),
        new(PermissionCodes.ProductLabelsManage, "Generar y reimprimir etiquetas", "CATALOGO"),
        new(PermissionCodes.CategoriesRead, "Consultar categorías", "CATALOGO"),
        new(PermissionCodes.CategoriesManage, "Gestionar categorías", "CATALOGO"),
        new(PermissionCodes.InventoryRead, "Consultar movimientos de inventario", "INVENTARIO"),
        new(PermissionCodes.InventoryAdjust, "Ajustar inventario manualmente", "INVENTARIO"),
        new(PermissionCodes.ReplenishmentAlertsRead, "Consultar alertas de reposición", "INVENTARIO"),
        new(PermissionCodes.CashOwnRead, "Consultar caja y movimientos propios", "CAJA"),
        new(PermissionCodes.CashShiftManage, "Abrir, arquear y cerrar caja", "CAJA"),
        new(PermissionCodes.CashMovementsManage, "Registrar movimientos manuales de caja", "CAJA"),
        new(PermissionCodes.CashShiftModeManage, "Configurar modalidad de turnos", "CAJA"),
        new(PermissionCodes.SalesCreate, "Realizar ventas y cobros", "VENTAS"),
        new(PermissionCodes.SalesOwnRead, "Consultar ventas propias", "VENTAS"),
        new(PermissionCodes.SalesAllRead, "Consultar historial general de ventas", "VENTAS"),
        new(PermissionCodes.SalesVoid, "Anular ventas", "VENTAS"),
        new(PermissionCodes.SalesReprint, "Reimprimir comprobantes", "VENTAS"),
        new(PermissionCodes.SalesDiscountApply, "Aplicar descuentos dentro del límite", "VENTAS"),
        new(PermissionCodes.SalesDiscountAuthorize, "Autorizar descuentos", "VENTAS"),
        new(PermissionCodes.SalesChangeAuthorize, "Autorizar cambios posteriores", "VENTAS"),
        new(PermissionCodes.CustomersBasicManage, "Crear y editar datos básicos de clientes", "CLIENTES"),
        new(PermissionCodes.CustomersManage, "Gestionar datos completos de clientes", "CLIENTES"),
        new(PermissionCodes.CustomersHistoryRead, "Consultar historial de clientes", "CLIENTES"),
        new(PermissionCodes.CreditRequest, "Solicitar ventas a crédito", "CREDITOS"),
        new(PermissionCodes.CreditAuthorize, "Autorizar créditos", "CREDITOS"),
        new(PermissionCodes.CreditManage, "Gestionar créditos y abonos", "CREDITOS"),
        new(PermissionCodes.CreditFutureBlock, "Bloquear crédito futuro", "CREDITOS"),
        new(PermissionCodes.LayawayManage, "Crear y gestionar apartados", "APARTADOS"),
        new(PermissionCodes.LayawayCancel, "Cancelar apartados", "APARTADOS"),
        new(PermissionCodes.LayawayRefundDecide, "Decidir devolución de apartados", "APARTADOS"),
        new(PermissionCodes.SuppliersRead, "Consultar proveedores", "COMPRAS"),
        new(PermissionCodes.SuppliersManage, "Gestionar proveedores", "COMPRAS"),
        new(PermissionCodes.PurchaseOrdersManage, "Gestionar pedidos de compra", "COMPRAS"),
        new(PermissionCodes.PurchaseOrdersCancel, "Cancelar pedidos enviados", "COMPRAS"),
        new(PermissionCodes.PurchasesReceive, "Registrar recepciones", "COMPRAS"),
        new(PermissionCodes.PurchasesReceiveExceptions, "Autorizar excepciones de recepción", "COMPRAS"),
        new(PermissionCodes.PurchasesCancel, "Cancelar compras confirmadas", "COMPRAS"),
        new(PermissionCodes.OperationalReportsRead, "Consultar reportes operativos", "REPORTES"),
        new(PermissionCodes.FinancialReportsRead, "Consultar reportes financieros", "REPORTES"),
        new(PermissionCodes.ConfigurationManage, "Gestionar configuración", "CONFIGURACION"),
        new(PermissionCodes.BackupsManage, "Gestionar respaldos y restauración", "CONFIGURACION"),
        new(PermissionCodes.PrintTest, "Ejecutar impresión de prueba", "CONFIGURACION"),
        new(PermissionCodes.PaymentMethodsManage, "Gestionar métodos de pago", "CONFIGURACION"),
        new(PermissionCodes.NumberingManage, "Gestionar numeración", "CONFIGURACION"),
        new(PermissionCodes.SynchronizationManage, "Gestionar sincronización", "CONFIGURACION"),
        new(PermissionCodes.TechnicalStatusRead, "Consultar estado técnico", "CONFIGURACION"),
        new(PermissionCodes.UpdatesManage, "Gestionar actualizaciones", "CONFIGURACION")
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RoleTemplates =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RoleCodes.Administrator] = Permissions.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase),
            [RoleCodes.Supervisor] = Set(
                PermissionCodes.CashierPasswordsReset,
                PermissionCodes.ProductsRead, PermissionCodes.ProductsCreate, PermissionCodes.ProductLabelsManage,
                PermissionCodes.CategoriesRead,
                PermissionCodes.InventoryRead, PermissionCodes.ReplenishmentAlertsRead,
                PermissionCodes.CashOwnRead, PermissionCodes.CashShiftManage, PermissionCodes.CashMovementsManage,
                PermissionCodes.CashShiftModeManage, PermissionCodes.SalesCreate, PermissionCodes.SalesOwnRead,
                PermissionCodes.SalesAllRead, PermissionCodes.SalesVoid, PermissionCodes.SalesReprint,
                PermissionCodes.SalesDiscountApply, PermissionCodes.SalesDiscountAuthorize,
                PermissionCodes.SalesChangeAuthorize, PermissionCodes.CustomersBasicManage, PermissionCodes.CustomersManage,
                PermissionCodes.CustomersHistoryRead, PermissionCodes.CreditRequest,
                PermissionCodes.CreditAuthorize, PermissionCodes.CreditManage, PermissionCodes.CreditFutureBlock,
                PermissionCodes.LayawayManage, PermissionCodes.LayawayCancel, PermissionCodes.SuppliersRead,
                PermissionCodes.PurchaseOrdersManage, PermissionCodes.PurchasesReceive,
                PermissionCodes.OperationalReportsRead, PermissionCodes.PrintTest),
            [RoleCodes.Cashier] = Set(
                PermissionCodes.ProductsRead, PermissionCodes.ReplenishmentAlertsRead,
                PermissionCodes.CashOwnRead, PermissionCodes.SalesCreate, PermissionCodes.SalesOwnRead,
                PermissionCodes.SalesReprint, PermissionCodes.SalesDiscountApply,
                PermissionCodes.CustomersBasicManage, PermissionCodes.CustomersHistoryRead,
                PermissionCodes.CreditRequest, PermissionCodes.CreditManage, PermissionCodes.LayawayManage,
                PermissionCodes.OperationalReportsRead)
        };

    public static IReadOnlySet<string> ForRole(string roleCode) =>
        RoleTemplates.TryGetValue(roleCode, out var permissions)
            ? permissions
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Set(params string[] codes) =>
        codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
