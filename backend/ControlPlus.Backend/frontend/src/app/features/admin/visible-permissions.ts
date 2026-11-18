/**
 * Permissions reserved for capabilities that are not administered from the V1 screen.
 * They remain in the server catalogue and keep protecting any implemented endpoint.
 */
export const HIDDEN_ADMIN_PERMISSION_CODES = new Set([
  'INVENTORY.REPLENISHMENT_ALERTS_READ',
  'REPORTS.OPERATIONAL_READ',
  'CREDIT.FUTURE_BLOCK',
  'PRINT.TEST',
  'PAYMENT_METHODS.MANAGE',
  'SYNCHRONIZATION.MANAGE',
  'TECHNICAL_STATUS.READ',
  'UPDATES.MANAGE',
]);

export const isVisibleAdminPermission = (code: string): boolean =>
  !HIDDEN_ADMIN_PERMISSION_CODES.has(code.toUpperCase());
