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
  const loaded = async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); };
  const create = async () => {
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); component.cashState.set(cash); await loaded();
    component.add(product); component.customers.set([customer]); component.customerId = customer.id;
  };
  beforeEach(() => {
    vi.clearAllMocks(); state.mockResolvedValue(cash); creditRules.mockResolvedValue({ defaultDays: 30 });
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ modalidad: 'credito' }) } } },
      { provide: SalesApi, useValue: { confirm: vi.fn(), creditRules } }, { provide: CashApi, useValue: { state } }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: SessionService, useValue: { has: (permission: string) => ['SALES.CREATE', 'CREDIT.REQUEST'].includes(permission), hasRole: (...codes: string[]) => codes.includes('CAJERO'), user: () => ({ id: '019a0000-0000-7000-8000-000000000003' }) } },
    ] });
  });

  it('lee el plazo del Administrador y propone la fecha de vencimiento desde la fecha de caja', async () => {
    await create();
    expect(creditRules).toHaveBeenCalledOnce(); expect(component.creditDefaultDays()).toBe(30); expect(component.creditRulesKnown()).toBe(true);
    expect(component.defaultDueDate).toBe('2026-11-23'); expect(component.defaultDueDateText).toBe('23/11/2026');
    component.startCheckout();
    expect(component.checkoutMode()).toBe(true); expect(component.dueDate).toBe('2026-11-23');
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

  it('propone fechas de cuotas distintas incluso para 90 cuotas', async () => {
    await create(); component.startCheckout(); component.setCreditPlan('INSTALLMENTS'); component.setInstallmentCount(90);
    expect(component.installmentDates).toHaveLength(90);
    expect(new Set(component.installmentDates).size).toBe(90);
    expect(component.installmentDates[0]).toBe(component.minimumDueDate);
    expect(component.installmentDates.at(-1)).toBe(component.maximumDueDate);
  });
});
