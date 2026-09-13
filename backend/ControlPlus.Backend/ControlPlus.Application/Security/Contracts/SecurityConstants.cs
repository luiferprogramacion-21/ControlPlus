namespace ControlPlus.Application.Security.Contracts;

/// <summary>
/// Stable role codes used by bootstrap data and JWT claims.
/// </summary>
public static class RoleCodes
{
    public const string Cashier = "CAJERO";
    public const string Supervisor = "SUPERVISOR";
    public const string Administrator = "ADMINISTRADOR";
}

/// <summary>
/// Stable permission codes used by authorization policies and JWT claims.
/// </summary>
public static class PermissionCodes
{
    // Permission.Code is normalized by Domain to uppercase, so policies and seed data use the same canonical values.
    public const string UsersRead = "USERS.READ";
    public const string UsersManage = "USERS.MANAGE";
    public const string CashierPasswordsReset = "USERS.CASHIER_PASSWORD_RESET";
    public const string RolesRead = "ROLES.READ";
    public const string RolesManage = "ROLES.MANAGE";
    public const string PermissionsRead = "PERMISSIONS.READ";
    public const string PermissionsManage = "PERMISSIONS.MANAGE";
    public const string UserPermissionsManage = "USER_PERMISSIONS.MANAGE";
    public const string AuditRead = "AUDIT.READ";
    public const string ProductsRead = "PRODUCTS.READ";
    public const string ProductsCreate = "PRODUCTS.CREATE";
    public const string ProductsSensitiveUpdate = "PRODUCTS.SENSITIVE_UPDATE";
    public const string ProductCostsRead = "PRODUCTS.COSTS_READ";
    public const string ProductLabelsManage = "PRODUCTS.LABELS_MANAGE";
    public const string CategoriesRead = "CATEGORIES.READ";
    public const string CategoriesManage = "CATEGORIES.MANAGE";
    public const string InventoryRead = "INVENTORY.READ";
    public const string InventoryAdjust = "INVENTORY.ADJUST";
    public const string ReplenishmentAlertsRead = "INVENTORY.REPLENISHMENT_ALERTS_READ";
    public const string CashOwnRead = "CASH.OWN_READ";
    public const string CashShiftManage = "CASH.SHIFT_MANAGE";
    public const string CashMovementsManage = "CASH.MOVEMENTS_MANAGE";
    public const string CashShiftModeManage = "CASH.SHIFT_MODE_MANAGE";
    public const string SalesCreate = "SALES.CREATE";
    public const string SalesOwnRead = "SALES.OWN_READ";
    public const string SalesAllRead = "SALES.ALL_READ";
    public const string SalesVoid = "SALES.VOID";
    public const string SalesReprint = "SALES.REPRINT";
    public const string SalesDiscountApply = "SALES.DISCOUNT_APPLY";
    public const string SalesDiscountAuthorize = "SALES.DISCOUNT_AUTHORIZE";
    public const string SalesChangeAuthorize = "SALES.CHANGE_AUTHORIZE";
    public const string CustomersBasicManage = "CUSTOMERS.BASIC_MANAGE";
    public const string CustomersManage = "CUSTOMERS.MANAGE";
    public const string CustomersHistoryRead = "CUSTOMERS.HISTORY_READ";
    public const string CreditRequest = "CREDIT.REQUEST";
    public const string CreditAuthorize = "CREDIT.AUTHORIZE";
    public const string CreditManage = "CREDIT.MANAGE";
    public const string CreditFutureBlock = "CREDIT.FUTURE_BLOCK";
    public const string LayawayManage = "LAYAWAY.MANAGE";
    public const string LayawayCancel = "LAYAWAY.CANCEL";
    public const string LayawayRefundDecide = "LAYAWAY.REFUND_DECIDE";
    public const string SuppliersRead = "SUPPLIERS.READ";
    public const string SuppliersManage = "SUPPLIERS.MANAGE";
    public const string PurchaseOrdersManage = "PURCHASE_ORDERS.MANAGE";
    public const string PurchaseOrdersCancel = "PURCHASE_ORDERS.CANCEL";
    public const string PurchasesReceive = "PURCHASES.RECEIVE";
    public const string PurchasesReceiveExceptions = "PURCHASES.RECEIVE_EXCEPTIONS";
    public const string PurchasesCancel = "PURCHASES.CANCEL";
    public const string OperationalReportsRead = "REPORTS.OPERATIONAL_READ";
    public const string FinancialReportsRead = "REPORTS.FINANCIAL_READ";
    public const string ConfigurationManage = "CONFIGURATION.MANAGE";
    public const string BackupsManage = "BACKUPS.MANAGE";
    public const string PrintTest = "PRINT.TEST";
    public const string PaymentMethodsManage = "PAYMENT_METHODS.MANAGE";
    public const string NumberingManage = "NUMBERING.MANAGE";
    public const string SynchronizationManage = "SYNCHRONIZATION.MANAGE";
    public const string TechnicalStatusRead = "TECHNICAL_STATUS.READ";
    public const string UpdatesManage = "UPDATES.MANAGE";
}
