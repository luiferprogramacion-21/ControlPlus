import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { SaleDraft, SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

const product = (id: string, extra: Record<string, unknown> = {}) =>
  ({ id, name: `Producto ${id}`, isActive: true, availableStock: 10, retailPrice: 1000, wholesalePrice: null, ...extra }) as any;
const cash = { hasOpenShift: true, requiresPreviousShiftClosure: false, requiresOperatorSession: false,
  currentShift: { operatingDate: '2026-11-18', responsibleUserId: 'u1' } } as any;
const draft: SaleDraft = {
  id: 'd1', saleType: 'CONTADO', customerId: 'c1', updatedAtUtc: '2026-11-18T15:30:00Z', discountReason: 'Cliente frecuente', generalDiscount: null,
  lines: [
    { productId: 'p1', quantity: 2, priceType: 'MINORISTA', discount: { type: 'PORCENTAJE', value: 5 } },
    { productId: 'p2', quantity: 1, priceType: 'MINORISTA', discount: null },
    { productId: 'p3', quantity: 4, priceType: 'MINORISTA', discount: null },
  ],
};

describe('Sales: venta recuperable', () => {
  let component: Sales;
  const api = {
    currentDraft: vi.fn(), saveDraft: vi.fn(), discardDraft: vi.fn(), product: vi.fn(), customer: vi.fn(),
    creditRules: vi.fn().mockResolvedValue({ defaultDays: 30, blockRiskyCredit: false }),
    pricingRules: vi.fn().mockResolvedValue({ wholesaleMinimumQuantity: 12 }),
  };
  const flush = async () => { for (let i = 0; i < 6; i++) await Promise.resolve(); };
  const create = async () => {
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); await flush();
  };

  beforeEach(() => {
    vi.clearAllMocks();
    api.currentDraft.mockResolvedValue(draft); api.saveDraft.mockResolvedValue(draft); api.discardDraft.mockResolvedValue(undefined);
    api.product.mockImplementation(async (id: string) => id === 'p2' ? product(id, { isActive: false }) : product(id));
    api.customer.mockResolvedValue({ id: 'c1', fullName: 'Ana', isActive: true });
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: SalesApi, useValue: api }, { provide: CashApi, useValue: { state: vi.fn().mockResolvedValue(cash) } },
      { provide: CatalogApi, useValue: {} }, { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: SessionService, useValue: { has: (code: string) => code === 'SALES.CREATE', hasRole: () => true, user: () => ({ id: 'u1', userName: 'admin' }) } },
    ] });
  });

  it('al entrar ofrece recuperar la venta sin terminar y no guarda nada hasta decidir', async () => {
    await create();
    expect(component.pendingDraft()?.id).toBe('d1');
    await (component as any).persistDraft();
    expect(api.saveDraft).not.toHaveBeenCalled();
  });

  it('recuperar arma el carrito, quita lo que ya no está disponible y carga el cliente', async () => {
    await create();
    await component.recoverDraft();
    expect(component.cart().map(line => [line.product.id, line.quantity, line.discount?.value ?? null])).toEqual([['p1', 2, 5], ['p3', 4, null]]);
    expect(component.customerId).toBe('c1');
    expect(component.discountReason).toBe('Cliente frecuente');
    expect(component.feedback()).toContain('Venta recuperada: 2 productos. 1 ya no está disponible y se quitó.');
    expect(component.pendingDraft()).toBeNull();
  });

  it('descartar cierra el borrador en el servidor', async () => {
    await create();
    await component.discardDraft();
    expect(api.discardDraft).toHaveBeenCalledOnce();
    expect(component.pendingDraft()).toBeNull();
    expect(component.feedback()).toBe('Se descartó la venta sin terminar.');
  });

  it('sin borrador guarda el carrito en curso y lo descarta al vaciarlo', async () => {
    api.currentDraft.mockResolvedValue(null);
    await create();
    component.cart.set([{ product: product('p1'), quantity: 3, priceType: 'MINORISTA', discount: null }]);
    component.customerId = 'c1';
    await (component as any).persistDraft();
    expect(api.saveDraft).toHaveBeenCalledWith({ saleType: 'CONTADO', customerId: 'c1', generalDiscount: null, discountReason: null,
      lines: [{ productId: 'p1', quantity: 3, priceType: 'MINORISTA', discount: null }] });
    component.cart.set([]);
    await (component as any).persistDraft();
    expect(api.discardDraft).toHaveBeenCalledOnce();
  });

  it('si el servidor no responde, la venta sigue sin errores en pantalla', async () => {
    api.currentDraft.mockRejectedValue(new Error('sin turno'));
    await create();
    api.saveDraft.mockRejectedValue(new Error('caído'));
    component.cart.set([{ product: product('p1'), quantity: 1, priceType: 'MINORISTA', discount: null }]);
    await (component as any).persistDraft();
    expect(component.error()).toBe('');
  });
});
