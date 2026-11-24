import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { CatalogApi } from '../catalog/catalog-api';
import { LayawayDetail, LayawaysApi } from './layaways-api';
import { LayawayProducts } from './layaway-products';

const detail = (balance: number): LayawayDetail => ({
  layaway: { id: 'l1', number: 'APT-001', customerId: 'c1', customerName: 'Ana', createdAtUtc: '', dueOn: '2026-12-01',
    total: 30000, initialPayment: 10000, balance, state: 'ACTIVO', version: 4, isOverdue: false },
  initialMinimumPercentage: 20,
  lines: [
    { productId: 'p1', productCode: 'ROP-1', productName: 'Camisa', unit: 'UNIDAD', quantity: 2, unitPrice: 10000, subtotal: 20000 },
    { productId: 'p2', productCode: 'ROP-2', productName: 'Gorra', unit: 'UNIDAD', quantity: 1, unitPrice: 10000, subtotal: 10000 },
  ],
  payments: [], delivery: null, cancellation: null,
});

describe('Corregir productos de un apartado', () => {
  let screen: LayawayProducts;
  let permissions: string[];
  const correctProducts = vi.fn();
  const products = vi.fn();
  const dialog = { open: vi.fn(async (prompt: AuthorizationPrompt) => { await prompt.run(null); return true; }) };

  const create = (balance: number) => {
    TestBed.configureTestingModule({ providers: [
      { provide: LayawaysApi, useValue: { correctProducts } },
      { provide: CatalogApi, useValue: { products } },
      { provide: AuthorizationDialog, useValue: dialog },
      { provide: SessionService, useValue: { has: (code: string) => permissions.includes(code), user: () => ({ userName: 'admin' }) } },
    ] });
    const fixture = TestBed.createComponent(LayawayProducts);
    fixture.componentRef.setInput('detail', detail(balance));
    fixture.detectChanges();
    screen = fixture.componentInstance;
  };

  beforeEach(() => {
    vi.clearAllMocks();
    permissions = ['LAYAWAY.MANAGE', 'LAYAWAY.CANCEL', 'LAYAWAY.REFUND_DECIDE'];
    correctProducts.mockResolvedValue({ id: 'k1', previousTotal: 30000, total: 20000, balance: 10000, refunded: 0 });
  });

  it('parte de los productos del apartado y no deja confirmar sin cambios ni motivo', () => {
    create(20000);
    expect(screen.rows().map(row => [row.productId, row.quantity])).toEqual([['p1', 2], ['p2', 1]]);
    expect(screen.validation()).toBe('No cambiaste ningún producto ni cantidad.');
    screen.step(screen.rows()[1], -1);
    expect(screen.validation()).toBe('Escribe el motivo de la corrección.');
  });

  it('quitar un producto baja el total y el saldo; se envían solo las líneas que quedan', async () => {
    create(20000);
    screen.step(screen.rows()[1], -1);
    screen.reason = 'El cliente ya no quiere la gorra';
    expect(screen.newTotal).toBe(20000);
    expect(screen.newBalance).toBe(10000);
    expect(screen.refund).toBe(0);
    let emitted: unknown = null; screen.corrected.subscribe(value => emitted = value);
    await screen.confirm();
    expect(correctProducts).toHaveBeenCalledWith(expect.objectContaining({ id: 'l1' }), {
      expectedVersion: 4, reason: 'El cliente ya no quiere la gorra', lines: [{ productId: 'p1', quantity: 2 }], refunds: [],
    }, expect.any(String));
    expect(emitted).toEqual(expect.objectContaining({ id: 'k1' }));
  });

  it('si lo abonado supera el nuevo total exige la devolución exacta y el permiso para devolver', () => {
    create(5000);
    screen.step(screen.rows()[0], -1); screen.step(screen.rows()[1], -1);
    screen.reason = 'Se equivocaron de talla';
    expect(screen.paid).toBe(25000);
    expect(screen.refund).toBe(15000);
    expect(screen.drafts[0].appliedAmount).toBe(15000);
    expect(screen.validation()).toBe('');
    // Con un solo medio, el medio lleva toda la devolución; repartido en dos, la suma debe ser exacta.
    screen.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 10000, receivedAmount: null, reference: '' }, { methodCode: 'NEQUI', appliedAmount: 2000, receivedAmount: null, reference: 'N-1' }];
    expect(screen.validation()).toContain('exactamente');
    screen.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 0, receivedAmount: null, reference: '' }];
    permissions = ['LAYAWAY.MANAGE'];
    expect(screen.validation()).toContain('cancelar apartados');
  });

  it('no deja quitar todos los productos: para eso está Cancelar apartado', () => {
    create(20000);
    screen.step(screen.rows()[0], -2); screen.step(screen.rows()[1], -1);
    screen.reason = 'Todo';
    expect(screen.validation()).toContain('Cancelar apartado');
  });

  it('agrega un producto encontrado sin superar lo disponible', async () => {
    create(20000);
    products.mockResolvedValue({ items: [{ id: 'p3', internalCode: 'ROP-3', name: 'Medias', retailPrice: 5000, availableStock: 1, isActive: true }] });
    screen.search = 'medias';
    await screen.searchProducts();
    screen.add(screen.results()[0]);
    const added = screen.rows().find(row => row.productId === 'p3')!;
    expect(added.original).toBe(0);
    screen.step(added, 1);
    expect(added.quantity).toBe(1);
    expect(screen.newTotal).toBe(35000);
  });
});
