import { HIDDEN_ADMIN_PERMISSION_CODES, isVisibleAdminPermission } from './visible-permissions';

describe('Administracion: permisos reservados', () => {
  it('oculta los ocho códigos que todavía no tienen función', () => {
    expect([...HIDDEN_ADMIN_PERMISSION_CODES].sort()).toEqual([
      'CREDIT.FUTURE_BLOCK',
      'INVENTORY.REPLENISHMENT_ALERTS_READ',
      'PAYMENT_METHODS.MANAGE',
      'PRINT.TEST',
      'REPORTS.OPERATIONAL_READ',
      'SYNCHRONIZATION.MANAGE',
      'TECHNICAL_STATUS.READ',
      'UPDATES.MANAGE',
    ]);
    for (const code of HIDDEN_ADMIN_PERMISSION_CODES) expect(isVisibleAdminPermission(code)).toBe(false);
  });

  it('conserva visibles los demas permisos', () => {
    expect(isVisibleAdminPermission('PRODUCTS.READ')).toBe(true);
    expect(isVisibleAdminPermission('PRODUCTS.LABELS_MANAGE')).toBe(true);
    expect(isVisibleAdminPermission('CONFIGURATION.MANAGE')).toBe(true);
  });
});
