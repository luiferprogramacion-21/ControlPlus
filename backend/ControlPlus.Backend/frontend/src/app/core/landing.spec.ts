import { landingNeedsCash, landingRoute } from './landing';

const make = (role: string, permissions: string[]) => ({
  can: (permission: string) => permissions.includes(permission),
  isRole: (...codes: string[]) => codes.includes(role),
});
const open = { hasOpenShift: true, requiresPreviousShiftClosure: false };
const closed = { hasOpenShift: false, requiresPreviousShiftClosure: false };
const previous = { hasOpenShift: true, requiresPreviousShiftClosure: true };

describe('pantalla de llegada al iniciar sesión (maqueta 00)', () => {
  it('Administrador y Supervisor con Dashboard llegan al Dashboard sin consultar Caja', () => {
    for (const role of ['ADMINISTRADOR', 'SUPERVISOR']) {
      const user = make(role, ['REPORTS.FINANCIAL_READ', 'SALES.CREATE', 'CASH.OWN_READ']);
      expect(landingNeedsCash(user.can, user.isRole)).toBe(false);
      expect(landingRoute(user.can, user.isRole, null)).toBe('/operacion/dashboard');
    }
  });

  it('quien vende llega al punto de venta con turno abierto y a Caja si falta abrirlo o cerrar el anterior', () => {
    const cashier = make('CAJERO', ['SALES.CREATE', 'CASH.OWN_READ']);
    expect(landingNeedsCash(cashier.can, cashier.isRole)).toBe(true);
    expect(landingRoute(cashier.can, cashier.isRole, open)).toBe('/operacion/ventas');
    expect(landingRoute(cashier.can, cashier.isRole, closed)).toBe('/operacion/caja');
    expect(landingRoute(cashier.can, cashier.isRole, previous)).toBe('/operacion/caja');
  });

  it('sin acceso a Caja o sin poder consultarla, va al punto de venta, que explica qué falta', () => {
    const noCash = make('CAJERO', ['SALES.CREATE']);
    expect(landingRoute(noCash.can, noCash.isRole, closed)).toBe('/operacion/ventas');
    const cashier = make('CAJERO', ['SALES.CREATE', 'CASH.OWN_READ']);
    expect(landingRoute(cashier.can, cashier.isRole, null)).toBe('/operacion/ventas');
  });

  it('un Supervisor sin Dashboard se trata como quien vende; sin ventas llega a Catálogo', () => {
    const supervisor = make('SUPERVISOR', ['SALES.CREATE', 'CASH.OWN_READ']);
    expect(landingRoute(supervisor.can, supervisor.isRole, open)).toBe('/operacion/ventas');
    const reader = make('CAJERO', ['PRODUCTS.READ']);
    expect(landingNeedsCash(reader.can, reader.isRole)).toBe(false);
    expect(landingRoute(reader.can, reader.isRole, null)).toBe('/operacion/catalogo');
  });
});
