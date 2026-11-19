import { homeTasks } from './home';
import { CashState } from '../cash/cash-api';

const cash = (overrides: Partial<CashState> = {}): CashState => ({ isConfigured: true, hasOpenShift: false, defaultShiftMode: 'INDIVIDUAL', requiresPreviousShiftClosure: false, requiresOperatorSession: false, guidance: null, currentShift: null, ownOperatorSession: null, ...overrides });
const ids = (permissions: string[], state: CashState | null) => homeTasks(p => permissions.includes(p), state).map(task => task.id);

describe('Inicio: tareas frecuentes', () => {
  it('con la caja cerrada, «Abrir caja» va primero y «Vender» pide abrirla', () => {
    const tasks = homeTasks(p => ['SALES.CREATE', 'CASH.SHIFT_MANAGE'].includes(p), cash());
    expect(tasks[0].id).toBe('abrir-caja');
    expect(tasks.find(t => t.id === 'vender')?.description).toBe('Primero abre la caja.');
    expect(tasks.some(t => t.id === 'cerrar-caja')).toBe(false);
  });

  it('con la caja abierta, «Vender» es la tarea principal y aparece «Cerrar caja»', () => {
    const tasks = homeTasks(p => ['SALES.CREATE', 'CASH.SHIFT_MANAGE'].includes(p), cash({ hasOpenShift: true }));
    expect(tasks.find(t => t.id === 'vender')?.primary).toBe(true);
    expect(tasks.some(t => t.id === 'abrir-caja')).toBe(false);
    expect(tasks.some(t => t.id === 'cerrar-caja')).toBe(true);
  });

  it('solo muestra lo que el rol puede hacer', () => {
    expect(ids(['PRODUCTS.READ'], null)).toEqual(['producto']);
    expect(ids(['CREDIT.MANAGE', 'LAYAWAY.MANAGE'], null)).toEqual(['abono', 'apartado']);
    expect(ids([], null)).toEqual([]);
  });

  it('un turno de otro día sin cerrar se ofrece primero', () => {
    expect(ids(['CASH.SHIFT_MANAGE', 'SALES.CREATE'], cash({ hasOpenShift: true, requiresPreviousShiftClosure: true }))[0]).toBe('cerrar-anterior');
  });
});
