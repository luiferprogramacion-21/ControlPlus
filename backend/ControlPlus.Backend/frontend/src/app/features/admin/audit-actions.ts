/** Nombres en español de las acciones auditadas (códigos de AuditAction en el backend), agrupados por módulo. */
export interface AuditActionGroup { module: string; actions: Record<string, string>; }

export const AUDIT_ACTION_GROUPS: AuditActionGroup[] = [
  { module: 'Seguridad', actions: {
    LoginSucceeded: 'Inicio de sesión', LoginFailed: 'Inicio de sesión fallido', UserLocked: 'Usuario bloqueado',
    UserReactivated: 'Acceso reactivado', PasswordChanged: 'Contraseña cambiada', InitialAdministratorRecovered: 'Recuperación del Administrador inicial',
    PasswordRecoveryRequested: 'Código de recuperación enviado por correo', PasswordRecoveryFailed: 'Código de recuperación incorrecto',
    PasswordRecovered: 'Contraseña recuperada por correo',
    UserCreated: 'Usuario creado', UserDeleted: 'Usuario eliminado', UserUpdated: 'Usuario modificado', UserActivated: 'Usuario activado', UserDeactivated: 'Usuario inactivado',
    RoleAssigned: 'Rol asignado', RoleRemoved: 'Rol retirado', RoleCreated: 'Rol creado', RoleUpdated: 'Rol modificado',
    RoleActivated: 'Rol activado', RoleDeactivated: 'Rol inactivado', PermissionCreated: 'Permiso creado', PermissionUpdated: 'Permiso modificado',
    PermissionActivated: 'Permiso activado', PermissionDeactivated: 'Permiso inactivado', PermissionGranted: 'Permiso concedido al rol',
    PermissionRevoked: 'Permiso retirado del rol', RolePermissionsReset: 'Permisos del rol restablecidos',
    UserPermissionGranted: 'Permiso concedido al usuario', UserPermissionRevoked: 'Permiso revocado al usuario',
    UserPermissionsReset: 'Permisos del usuario restablecidos', OperatorCredentialIssued: 'Carné de operador emitido',
  } },
  { module: 'Catálogo', actions: {
    CategoryCreated: 'Categoría creada', CategoryUpdated: 'Categoría modificada', CategoryActivated: 'Categoría activada',
    CategoryDeactivated: 'Categoría inactivada', ProductCreated: 'Producto creado', ProductUpdated: 'Producto modificado',
    ProductActivated: 'Producto activado', ProductDeactivated: 'Producto inactivado', ProductDeleted: 'Producto borrado',
  } },
  { module: 'Caja', actions: {
    CashRegisterConfigured: 'Caja configurada', CashShiftModeUpdated: 'Modalidad de turno cambiada', CashShiftOpened: 'Turno abierto',
    CashMovementRegistered: 'Movimiento de caja', CashReconciled: 'Arqueo', CashShiftClosed: 'Turno cerrado',
    CashOpenAuthorizationRejected: 'Autorización de apertura rechazada', CashCloseAuthorizationRejected: 'Autorización de cierre rechazada',
    OperatorSessionStarted: 'Sesión de operador iniciada', OperatorSessionClosed: 'Sesión de operador cerrada',
  } },
  { module: 'Ventas', actions: {
    SaleConfirmed: 'Venta confirmada', SaleCancelled: 'Venta anulada', SaleChanged: 'Cambio de venta', SaleReprintRequested: 'Reimpresión de tiquete',
    SaleCancellationAuthorizationRejected: 'Autorización de anulación rechazada', SaleChangeAuthorizationRejected: 'Autorización de cambio rechazada',
    SaleDiscountAuthorizationRejected: 'Autorización de descuento rechazada', SaleCancellationReasonsInitialized: 'Motivos de anulación completados',
    SaleChangePolicyUpdated: 'Política de cambios modificada', SalesSettingsUpdated: 'Mínimo mayorista modificado',
    SalesDocumentNumberingCreated: 'Numeración de ventas creada', SalesDocumentNumberingUpdated: 'Numeración de ventas modificada',
    SaleChangeNumberingCreated: 'Numeración de cambios creada', VatSettingUpdated: 'IVA del negocio modificado',
  } },
  { module: 'Inventario', actions: {
    InventoryAdjusted: 'Ajuste de inventario', InventoryAdjustmentAuthorizationRejected: 'Autorización de ajuste rechazada',
  } },
  { module: 'Clientes y créditos', actions: {
    CustomerCreated: 'Cliente creado', CustomerUpdated: 'Cliente modificado', CustomerDeactivated: 'Cliente inactivado',
    CustomerReactivated: 'Cliente reactivado', CreditCreated: 'Crédito otorgado', CreditPaymentApplied: 'Abono a crédito',
    CreditAuthorizationRejected: 'Autorización de crédito rechazada', CreditSettingsUpdated: 'Plazo de créditos modificado',
    CreditPaymentCancelled: 'Abono a crédito anulado', CreditPaymentCancellationAuthorizationRejected: 'Autorización de anulación de abono rechazada',
    CreditForgiven: 'Saldo de crédito condonado',
    CreditPaymentNumberingCreated: 'Numeración de abonos creada', CreditPaymentNumberingUpdated: 'Numeración de abonos modificada',
  } },
  { module: 'Apartados', actions: {
    LayawayCreated: 'Apartado creado', LayawayPaymentApplied: 'Abono a apartado', LayawayDelivered: 'Apartado entregado',
    LayawayCancelled: 'Apartado cancelado', LayawayAuthorizationRejected: 'Autorización de apartado rechazada',
    LayawaySettingsUpdated: 'Reglas de apartados modificadas', LayawayNumberingCreated: 'Numeración de apartados creada',
    LayawayPaymentNumberingCreated: 'Numeración de abonos de apartados creada',
  } },
  { module: 'Compras y proveedores', actions: {
    SupplierCreated: 'Proveedor creado', SupplierUpdated: 'Proveedor modificado', SupplierDeactivated: 'Proveedor inactivado',
    SupplierReactivated: 'Proveedor reactivado', PurchaseDraftCreated: 'Borrador de compra creado', PurchaseDraftUpdated: 'Borrador de compra modificado',
    PurchaseConfirmed: 'Compra confirmada', PurchaseCancelled: 'Compra anulada', PurchaseCancellationAuthorizationRejected: 'Autorización de anulación de compra rechazada',
    PurchaseOrderCreated: 'Pedido creado', PurchaseOrderUpdated: 'Pedido modificado', PurchaseOrderSent: 'Pedido enviado',
    PurchaseOrderReceived: 'Recepción de pedido', PurchaseOrderClosed: 'Pedido cerrado', PurchaseOrderCancelled: 'Pedido cancelado',
    PurchaseOrderAuthorizationRejected: 'Autorización de pedido rechazada', PurchaseReceiptAuthorizationRejected: 'Autorización de recepción rechazada',
    PurchaseOrderReasonsInitialized: 'Motivos de pedidos completados', PurchaseDocumentNumberingCreated: 'Numeración de compras creada',
    PurchaseDocumentNumberingUpdated: 'Numeración de compras modificada', PurchaseOrderNumberingCreated: 'Numeración de pedidos creada',
  } },
  { module: 'Impresión', actions: {
    PrintProfileCreated: 'Perfil de impresión creado', PrintJobQueued: 'Impresión en cola', PrintJobAccepted: 'Impresión aceptada',
    PrintJobFailed: 'Impresión fallida', PrintJobRetried: 'Impresión reintentada', PrintJobUncertain: 'Impresión con resultado incierto',
  } },
  { module: 'Respaldos', actions: {
    BackupRequested: 'Respaldo manual solicitado', BackupDestinationUpdated: 'Destino de respaldo modificado',
    BackupRestoreTested: 'Prueba de restauración',
  } },
  { module: 'Configuración', actions: {
    EstablishmentUpdated: 'Datos del establecimiento modificados',
  } },
];

const LABELS: Record<string, string> = Object.assign({}, ...AUDIT_ACTION_GROUPS.map((group) => group.actions));
const MODULES: Record<string, string> = Object.assign({}, ...AUDIT_ACTION_GROUPS.map((group) =>
  Object.fromEntries(Object.keys(group.actions).map((code) => [code, group.module]))));

/** Nombre en español; una acción nueva sin traducir muestra su código separado en palabras. */
export function auditActionLabel(code: string): string {
  return LABELS[code] ?? code.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function auditActionModule(code: string): string {
  return MODULES[code] ?? 'Otros';
}
