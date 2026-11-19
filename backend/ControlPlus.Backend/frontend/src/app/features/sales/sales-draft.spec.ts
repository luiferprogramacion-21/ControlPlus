import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { HeldSale, SaleDraft, SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';

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
const held = (id: string, extra: Partial<HeldSale> = {}): HeldSale => ({
  id, saleType: 'CONTADO', customerId: null, customerName: null, products: 1, units: 1, approximateTotal: 1000,
  updatedAtUtc: '2026-11-19T15:30:00Z', ...extra,
});
const line = (id: string, quantity = 1) => ({ product: product(id), quantity, priceType: 'MINORISTA' as const, discount: null });

describe('Sales: venta recuperable y ventas en espera', () => {
  let component: Sales;
  const confirm = vi.fn();
  const api = {
    heldSales: vi.fn(), draft: vi.fn(), createDraft: vi.fn(), updateDraft: vi.fn(), discardDraft: vi.fn(),
    product: vi.fn(), customer: vi.fn(),
    creditRules: vi.fn().mockResolvedValue({ defaultDays: 30, blockRiskyCredit: false }),
    pricingRules: vi.fn().mockResolvedValue({ wholesaleMinimumQuantity: 12 }),
  };
  const flush = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };
  const create = async () => {
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); await flush();
  };
  const persist = () => (component as unknown as { persistDraft(): Promise<void> }).persistDraft();

  beforeEach(() => {
    vi.clearAllMocks();
    confirm.mockResolvedValue(true);
    api.heldSales.mockResolvedValue([held('d1', { customerName: 'Ana', products: 3, units: 7 })]);
    api.draft.mockResolvedValue(draft);
    api.createDraft.mockImplementation(async () => ({ ...draft, id: 'nuevo' }));
    api.updateDraft.mockResolvedValue(draft);
    api.discardDraft.mockResolvedValue(undefined);
    api.product.mockImplementation(async (id: string) => id === 'p2' ? product(id, { isActive: false }) : product(id));
    api.customer.mockResolvedValue({ id: 'c1', fullName: 'Ana', isActive: true });
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      { provide: SalesApi, useValue: api }, { provide: CashApi, useValue: { state: vi.fn().mockResolvedValue(cash) } },
      { provide: CatalogApi, useValue: {} }, { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: ConfirmationDialog, useValue: { confirm } },
      { provide: SessionService, useValue: { has: (code: string) => code === 'SALES.CREATE', hasRole: () => true, user: () => ({ id: 'u1', userName: 'admin' }) } },
    ] });
  });

  it('al entrar avisa de la venta sin terminar y no guarda nada mientras el carrito está vacío', async () => {
    await create();
    expect(component.heldNotice()).toBe(true);
    expect(component.heldSales().map(item => item.id)).toEqual(['d1']);
    await persist();
    expect(api.createDraft).not.toHaveBeenCalled();
    expect(api.updateDraft).not.toHaveBeenCalled();
    expect(api.discardDraft).not.toHaveBeenCalled();
  });

  it('retomar arma el carrito, quita lo que ya no está disponible y sigue guardando en ese mismo borrador', async () => {
    await create();
    await component.resumeHeld(component.heldSales()[0]);
    expect(api.draft).toHaveBeenCalledWith('d1');
    expect(component.cart().map(item => [item.product.id, item.quantity, item.discount?.value ?? null])).toEqual([['p1', 2, 5], ['p3', 4, null]]);
    expect(component.customerId).toBe('c1');
    expect(component.discountReason).toBe('Cliente frecuente');
    expect(component.feedback()).toContain('Venta retomada: 2 productos. 1 ya no está disponible y se quitó.');
    expect(component.heldNotice()).toBe(false);
    expect(component.heldSales()).toEqual([]);
    await persist();
    expect(api.updateDraft).toHaveBeenCalledWith('d1', expect.objectContaining({ customerId: 'c1' }));
    expect(api.createDraft).not.toHaveBeenCalled();
  });

  it('descartar una venta en espera pide confirmación y la cierra en el servidor', async () => {
    await create();
    confirm.mockResolvedValueOnce(false);
    await component.discardHeld(component.heldSales()[0]);
    expect(api.discardDraft).not.toHaveBeenCalled();
    api.heldSales.mockResolvedValue([]);
    await component.discardHeld(component.heldSales()[0]);
    expect(confirm.mock.calls[1][0].message).toContain('Ana (3 productos)');
    expect(api.discardDraft).toHaveBeenCalledWith('d1');
    expect(component.heldSales()).toEqual([]);
    expect(component.heldNotice()).toBe(false);
  });

  it('empezar otra venta oculta el aviso sin descartar la que quedó en espera', async () => {
    await create();
    component.cart.set([line('p1')]);
    TestBed.tick();
    expect(component.heldNotice()).toBe(false);
    expect(api.discardDraft).not.toHaveBeenCalled();
    expect(component.heldSales().map(item => item.id)).toEqual(['d1']);
  });

  it('un carrito nuevo se guarda como otro borrador, se actualiza y se descarta al vaciarlo', async () => {
    api.heldSales.mockResolvedValue([]);
    await create();
    component.cart.set([line('p1', 3)]);
    component.customerId = 'c1';
    await persist();
    expect(api.createDraft).toHaveBeenCalledWith({ saleType: 'CONTADO', customerId: 'c1', generalDiscount: null, discountReason: null,
      lines: [{ productId: 'p1', quantity: 3, priceType: 'MINORISTA', discount: null }] });
    await persist();
    expect(api.updateDraft).toHaveBeenCalledWith('nuevo', expect.anything());
    component.cart.set([]);
    await persist();
    expect(api.discardDraft).toHaveBeenCalledWith('nuevo');
  });

  it('poner en espera guarda la venta, deja la pantalla libre y la muestra en la lista', async () => {
    api.heldSales.mockResolvedValue([]);
    await create();
    component.cart.set([line('p1', 2)]);
    api.heldSales.mockResolvedValue([held('nuevo', { units: 2 })]);
    await component.holdSale();
    expect(api.createDraft).toHaveBeenCalledOnce();
    expect(component.cart()).toEqual([]);
    expect(component.customerId).toBe('');
    expect(component.heldSales().map(item => item.id)).toEqual(['nuevo']);
    expect(component.feedback()).toContain('Venta puesta en espera');
    // La pantalla vacía no descarta la venta que quedó en espera.
    await persist();
    expect(api.discardDraft).not.toHaveBeenCalled();
  });

  it('si no se puede poner en espera, la venta sigue en pantalla y se explica', async () => {
    api.heldSales.mockResolvedValue([]);
    await create();
    component.cart.set([line('p1')]);
    api.createDraft.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'Ya tienes 10 ventas en espera.' } }));
    await component.holdSale();
    expect(component.cart().length).toBe(1);
    expect(component.error()).toContain('No se pudo poner en espera.');
  });

  it('retomar con una venta en pantalla la deja en espera primero', async () => {
    api.heldSales.mockResolvedValue([]);
    await create();
    component.cart.set([line('p3')]);
    await component.resumeHeld(held('d1'));
    expect(api.createDraft).toHaveBeenCalledWith(expect.objectContaining({ lines: [expect.objectContaining({ productId: 'p3' })] }));
    expect(api.createDraft.mock.invocationCallOrder[0]).toBeLessThan(api.draft.mock.invocationCallOrder[0]);
    expect(component.cart().map(item => item.product.id)).toEqual(['p1', 'p3']);
  });

  it('un borrador cerrado en otra pantalla se reemplaza por uno nuevo', async () => {
    api.heldSales.mockResolvedValue([]);
    await create();
    component.cart.set([line('p1')]);
    await persist();
    api.updateDraft.mockRejectedValueOnce(new HttpErrorResponse({ status: 404 }));
    api.createDraft.mockResolvedValueOnce({ ...draft, id: 'otro' });
    await persist();
    expect(api.createDraft).toHaveBeenCalledTimes(2);
    await persist();
    expect(api.updateDraft).toHaveBeenLastCalledWith('otro', expect.anything());
  });

  it('si el servidor no responde, la venta sigue sin errores en pantalla', async () => {
    api.heldSales.mockRejectedValue(new Error('sin turno'));
    await create();
    api.createDraft.mockRejectedValue(new Error('caído'));
    component.cart.set([line('p1')]);
    await persist();
    expect(component.error()).toBe('');
    expect(component.heldNotice()).toBe(false);
  });
});
