import { TestBed } from '@angular/core/testing';
import { Purchases } from './purchases';
import { PurchasesApi } from './purchases-api';
import { OrdersApi } from './orders-api';
import { SuppliersApi } from '../suppliers/suppliers-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';

describe('Purchases', () => {
  let component: Purchases;
  let receive: ReturnType<typeof vi.fn>;
  let getOrder: ReturnType<typeof vi.fn>;
  let listOrders: ReturnType<typeof vi.fn>;
  const orderId = '019a2469-7680-7000-8000-000000000001';
  const supplierId = '019a2469-7680-7000-8000-000000000002';
  const lineId = '019a2469-7680-7000-8000-000000000003';
  const productId = '019a2469-7680-7000-8000-000000000004';
  beforeEach(() => {
    receive = vi.fn();
    getOrder = vi.fn();
    listOrders = vi.fn();
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: {} }, { provide: OrdersApi, useValue: { receive, get: getOrder, list: listOrders } },
      { provide: AuthorizationDialog, useValue: { open: vi.fn() } },
      { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
      { provide: SessionService, useValue: { has: (permission: string) => permission === 'PURCHASES.RECEIVE' || permission === 'PURCHASE_ORDERS.MANAGE', hasRole: (...codes: string[]) => codes.includes('SUPERVISOR'), user: () => ({ roles: ['SUPERVISOR'] }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Purchases());
    component.selectedOrder.set({ id: orderId, version: 5, supplierId, status: 'RECIBIDO_PARCIAL', visibleNumber: 'PED-1', createdAt: '2026-10-24T12:00:00Z', sentAt: '2026-10-24T12:10:00Z', closedAt: null, lines: [
      { id: lineId, productId, productCode: 'P1', productName: 'Producto', requestedQuantity: 10, receivedQuantity: 6, pendingQuantity: 4, excessQuantity: 0, suggestedQuantity: 10, estimatedUnitCost: 100, observation: null },
    ] });
    component.startReceipt(); component.receipt.invoiceDate = '2026-10-24';
  });
  it('rechaza cantidad mayor que la pendiente sin enviar HTTP', async () => { component.receipt.lines[0].quantity = 5; expect(component.receiptValidation()).toContain('pendiente'); await component.receive(); expect(receive).not.toHaveBeenCalled(); });
  it('rechaza cantidades fraccionarias y costo inválido', () => { component.receipt.lines[0].quantity = 1.5; expect(component.receiptValidation()).toContain('entera'); component.receipt.lines[0].quantity = 2; component.receipt.lines[0].unitCost = 0; expect(component.receiptValidation()).toContain('costo'); });
  it('impide doble envío mientras la recepción está pendiente', async () => {
    component.receipt.lines[0].quantity = 2;
    expect(component.receiptValidation()).toBe('');
    component.tab = 'orders';
    let resolveFirst!: (value: unknown) => void;
    receive.mockReturnValue(new Promise(resolve => { resolveFirst = resolve; }));
    const first = component.receive();
    expect(component.saving()).toBe(true);
    expect(receive).toHaveBeenCalledTimes(1);
    const [submittedOrder, idempotencyKey, payload] = receive.mock.calls[0];
    expect(submittedOrder).toMatchObject({ id: orderId, status: 'RECIBIDO_PARCIAL', version: 5 });
    expect(idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(payload).toMatchObject({ invoiceDate: '2026-10-24', vatMode: 'NO_DISCRIMINADO', lines: [{ orderLineId: lineId, productId, quantity: 2, unitCost: 100, vatPercentage: null }] });

    await component.receive();
    expect(receive).toHaveBeenCalledTimes(1);
    expect(component.saving()).toBe(true);

    const updatedOrder = { ...submittedOrder, version: 6, lines: [{ ...submittedOrder.lines[0], receivedQuantity: 8, pendingQuantity: 2 }] };
    getOrder.mockResolvedValue(updatedOrder);
    listOrders.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    resolveFirst({ purchase: { id: '019a2469-7680-7000-8000-000000000005' }, orderId, orderStatus: 'RECIBIDO_PARCIAL', orderVersion: 6 });
    await first;
    expect(receive).toHaveBeenCalledTimes(1);
    expect(component.saving()).toBe(false);
    expect(component.receiving()).toBe(false);
    expect(component.selectedOrder()?.version).toBe(6);
    expect(getOrder).toHaveBeenCalledOnce();
    expect(listOrders).toHaveBeenCalledOnce();
  });
});
