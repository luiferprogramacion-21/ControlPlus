import { TestBed } from '@angular/core/testing';
import { convertToParamMap, ActivatedRoute } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

const product = { id: '019a0000-0000-7000-8000-000000000001', isActive: true, availableStock: 5, retailPrice: 10000, wholesalePrice: null } as any;
const customer = { id: '019a0000-0000-7000-8000-000000000002', fullName: 'Ana', isActive: true, phone: '3001234567', documentType: null, documentNumber: null } as any;
const cash = { hasOpenShift: true, requiresPreviousShiftClosure: false, requiresOperatorSession: false, currentShift: { operatingDate: '2026-10-24', responsibleUserId: '019a0000-0000-7000-8000-000000000003' } } as any;

describe('Sales: plazo predeterminado del vencimiento del crédito', () => {
  let component: Sales;
  const creditRules = vi.fn();
  const state = vi.fn();
  const confirmSale = vi.fn();
  const authorize = vi.fn();
  const deferredRules = () => {
    let resolve!: (rules: { defaultDays: number | null; defaultMonths?: number | null }) => void;
    let reject!: (error: Error) => void;
    const promise = new Promise<{ defaultDays: number | null; defaultMonths?: number | null }>((done, fail) => { resolve = done; reject = fail; });
    return { promise, resolve, reject };
  };
  const loaded = async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); };
  const create = async (operatingDate = '2026-10-24') => {
    const activeCash = { ...cash, currentShift: { ...cash.currentShift, operatingDate } };
    state.mockResolvedValue(activeCash);
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); component.cashState.set(activeCash); await loaded();
    component.add(product); component.customers.set([customer]); component.customerId = customer.id;
  };
  beforeEach(() => {
    vi.resetAllMocks(); state.mockResolvedValue(cash); creditRules.mockResolvedValue({ defaultDays: 30 });
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ modalidad: 'credito' }) } } },
      { provide: SalesApi, useValue: { confirm: confirmSale, creditRules } }, { provide: CashApi, useValue: { state } }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: { open: authorize } },
      { provide: SessionService, useValue: { has: (permission: string) => ['SALES.CREATE', 'CREDIT.REQUEST'].includes(permission), hasRole: (...codes: string[]) => codes.includes('CAJERO'), user: () => ({ id: '019a0000-0000-7000-8000-000000000003' }) } },
    ] });
  });

  it('lee el plazo del Administrador y propone la fecha de vencimiento desde la fecha de caja', async () => {
    await create();
    expect(creditRules).toHaveBeenCalledOnce(); expect(component.creditDefaultDays()).toBe(30); expect(component.creditRulesKnown()).toBe(true);
    expect(component.defaultDueDate).toBe('2026-11-23'); expect(component.defaultDueDateText).toBe('23/11/2026');
    component.startCheckout();
    expect(component.checkoutMode()).toBe(true); expect(component.dueDate).toBe('2026-11-23');
    await loaded();
    expect(creditRules).toHaveBeenCalledTimes(2); expect(component.creditRulesLoading()).toBe(false);
  });

  it('sugiere tres meses calendario desde la fecha operativa, muestra la unidad y admite fechas posteriores a 90 días', async () => {
    creditRules.mockResolvedValue({ defaultDays: null, defaultMonths: 3 });
    await create('2026-11-21');
    expect(component.creditDefaultMonths()).toBe(3);
    expect(component.defaultCreditTermText).toBe('3 meses');
    expect(component.defaultDueDate).toBe('2027-02-21');
    expect(component.defaultDueDateText).toBe('21/02/2027');
    expect(component.maximumDueDate).toBe('2027-02-21');

    component.startCheckout(); await loaded();
    expect(component.dueDate).toBe('2027-02-21');
    expect(component.canConfirm).toBe(true);
    component.setDueDate('2027-02-20'); expect(component.canConfirm).toBe(true);
    component.setDueDate('2027-02-22'); expect(component.canConfirm).toBe(false);
  });

  it('ajusta al último día del mes si el mes de vencimiento no tiene ese día', async () => {
    creditRules.mockResolvedValue({ defaultDays: null, defaultMonths: 3 });
    await create('2027-01-31');
    expect(component.defaultDueDate).toBe('2027-04-30');
    component.startCheckout(); await loaded();
    expect(component.dueDate).toBe('2027-04-30');
    expect(component.defaultDueDateText).toBe('30/04/2027');
  });

  it.each([
    [{ defaultDays: null, defaultMonths: 1 }, '1 mes', '2026-12-21'],
    [{ defaultDays: 1, defaultMonths: null }, '1 día', '2026-11-22'],
  ])('muestra la unidad singular del plazo %s', async (rules, label, dueDate) => {
    creditRules.mockResolvedValue(rules);
    await create('2026-11-21');
    expect(component.defaultCreditTermText).toBe(label);
    expect(component.defaultDueDate).toBe(dueDate);
  });

  it('la fecha propuesta se puede cambiar y una fecha ya elegida no se pisa', async () => {
    await create();
    component.dueDate = '2026-11-08'; component.startCheckout();
    expect(component.dueDate).toBe('2026-11-08');
    component.dueDate = '2026-12-01'; expect(component.dueDate).toBe('2026-12-01');
  });

  it('un plazo mayor a 90 días se propone como máximo en 90 días', async () => {
    creditRules.mockResolvedValue({ defaultDays: 120 }); await create();
    expect(component.defaultDueDate).toBe('2027-01-22'); expect(component.defaultDueDate).toBe(component.maximumDueDate);
  });

  it('sin plazo predeterminado no propone fecha y sigue siendo obligatoria', async () => {
    creditRules.mockResolvedValue({ defaultDays: null }); await create();
    expect(component.creditDefaultDays()).toBeNull(); expect(component.creditRulesKnown()).toBe(true); expect(component.defaultDueDate).toBe('');
    component.startCheckout(); expect(component.dueDate).toBe(''); expect(component.canConfirm).toBe(false);
  });

  it('si no se pueden leer las reglas no bloquea: el servidor valida el vencimiento', async () => {
    creditRules.mockRejectedValue(new Error('sin respuesta')); await create();
    expect(component.creditRulesKnown()).toBe(false); expect(component.defaultDueDate).toBe('');
    component.startCheckout(); expect(component.checkoutMode()).toBe(true); expect(component.dueDate).toBe('');
  });

  it('una venta de contado no recibe fecha de vencimiento', async () => {
    await create(); component.startCashSale();
    expect(component.saleType()).toBe('CONTADO'); expect(component.dueDate).toBe('');
  });

  it('al volver al cobro actualiza la fecha automática y el texto con el plazo vigente de 90 días', async () => {
    await create(); component.startCheckout(); await loaded();
    expect(component.dueDate).toBe('2026-11-23');
    component.returnToSale(); creditRules.mockResolvedValue({ defaultDays: 90 });
    component.startCheckout(); await loaded();
    expect(component.creditDefaultDays()).toBe(90);
    expect(component.dueDate).toBe('2027-01-22');
    expect(component.defaultDueDateText).toBe('22/01/2027');
  });

  it('si el Administrador cambia de días a meses antes del cobro, actualiza fecha y texto', async () => {
    await create('2026-11-21');
    component.startCheckout(); await loaded();
    expect(component.defaultCreditTermText).toBe('30 días');
    expect(component.dueDate).toBe('2026-12-21');

    component.returnToSale();
    creditRules.mockResolvedValue({ defaultDays: null, defaultMonths: 3 });
    component.startCheckout(); await loaded();
    expect(component.creditDefaultDays()).toBeNull();
    expect(component.creditDefaultMonths()).toBe(3);
    expect(component.defaultCreditTermText).toBe('3 meses');
    expect(component.dueDate).toBe('2027-02-21');
  });

  it('carga la fecha aunque las reglas iniciales todavía no hayan llegado al abrir el cobro', async () => {
    const initial = deferredRules(); const checkout = deferredRules();
    creditRules.mockReturnValueOnce(initial.promise).mockReturnValueOnce(checkout.promise);
    await create(); component.startCheckout();
    expect(component.dueDate).toBe(''); expect(component.creditRulesLoading()).toBe(true);
    initial.resolve({ defaultDays: 30 }); await loaded();
    expect(component.creditRulesLoading()).toBe(true); expect(component.dueDate).toBe('');
    checkout.resolve({ defaultDays: 90 }); await loaded();
    expect(component.dueDate).toBe('2027-01-22'); expect(component.creditRulesLoading()).toBe(false);
  });

  it('una respuesta inicial más antigua no sustituye la regla nueva del cobro', async () => {
    const initial = deferredRules(); creditRules.mockReturnValueOnce(initial.promise);
    await create(); creditRules.mockResolvedValue({ defaultDays: 90 });
    component.startCheckout(); await loaded();
    initial.resolve({ defaultDays: 30 }); await loaded();
    expect(component.creditDefaultDays()).toBe(90);
    expect(component.dueDate).toBe('2027-01-22'); expect(component.defaultDueDateText).toBe('22/01/2027');
  });

  it.each(['2026-11-23', '2026-12-01', ''])('preserva la fecha editada «%s» mientras espera reglas nuevas', async (date) => {
    await create(); component.startCheckout(); await loaded(); component.returnToSale();
    const checkout = deferredRules(); creditRules.mockReturnValueOnce(checkout.promise);
    component.startCheckout(); component.setDueDate(date);
    checkout.resolve({ defaultDays: 90 }); await loaded();
    expect(component.creditDefaultDays()).toBe(90); expect(component.defaultDueDateText).toBe('22/01/2027');
    expect(component.dueDate).toBe(date);
  });

  it('al desactivar el plazo predeterminado retira una sugerencia automática anterior', async () => {
    await create(); component.startCheckout(); await loaded(); component.returnToSale();
    creditRules.mockResolvedValue({ defaultDays: null }); component.startCheckout(); await loaded();
    expect(component.creditRulesKnown()).toBe(true); expect(component.creditDefaultDays()).toBeNull();
    expect(component.dueDate).toBe(''); expect(component.defaultDueDateText).toBe(''); expect(component.canConfirm).toBe(false);
  });

  it('un fallo de recarga descarta la regla anterior y permite escoger el vencimiento manualmente', async () => {
    await create(); component.startCheckout(); await loaded(); component.returnToSale();
    creditRules.mockRejectedValueOnce(new Error('sin respuesta')); component.startCheckout(); await loaded();
    expect(component.creditRulesKnown()).toBe(false); expect(component.creditDefaultDays()).toBeNull();
    expect(component.dueDate).toBe(''); expect(component.creditRulesLoading()).toBe(false);
    component.setDueDate('2026-12-01'); expect(component.canConfirm).toBe(true);
  });

  it('no aplica a otro crédito la respuesta pendiente de una venta cancelada', async () => {
    await create(); const previous = deferredRules(); creditRules.mockReturnValueOnce(previous.promise);
    component.startCheckout(); component.returnToSale(); component.cancelSale(); component.newSale();
    previous.resolve({ defaultDays: 90 }); await loaded();
    expect(component.saleType()).toBe('CONTADO'); expect(component.dueDate).toBe('');
    component.add(product); component.customers.set([customer]); component.customerId = customer.id;
    creditRules.mockResolvedValue({ defaultDays: 45 }); component.startCreditSale(); await loaded();
    expect(component.creditDefaultDays()).toBe(45); expect(component.dueDate).toBe('2026-12-08');
  });

  it('una respuesta del cobro abandonado no sobreescribe la del siguiente crédito', async () => {
    await create(); const previous = deferredRules(); creditRules.mockReturnValueOnce(previous.promise);
    component.startCheckout(); component.returnToSale(); component.cancelSale(); component.newSale();
    component.add(product); component.customers.set([customer]); component.customerId = customer.id;
    creditRules.mockResolvedValue({ defaultDays: 45 }); component.startCreditSale(); await loaded();
    previous.resolve({ defaultDays: 90 }); await loaded();
    expect(component.creditDefaultDays()).toBe(45); expect(component.dueDate).toBe('2026-12-08');
  });

  it('espera las reglas vigentes antes de autorizar o confirmar el crédito', async () => {
    await create(); const checkout = deferredRules(); creditRules.mockReturnValueOnce(checkout.promise);
    component.startCheckout();
    expect(component.creditRulesLoading()).toBe(true); expect(component.canConfirm).toBe(false);
    await component.confirm();
    expect(authorize).not.toHaveBeenCalled(); expect(confirmSale).not.toHaveBeenCalled();
    checkout.resolve({ defaultDays: 90 }); await loaded();
    expect(component.canConfirm).toBe(true);
  });
});
