import { TestBed } from '@angular/core/testing';
import { Purchases } from './purchases';
import { PurchasesApi } from './purchases-api';
import { OrdersApi } from './orders-api';
import { SuppliersApi } from '../suppliers/suppliers-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { PurchaseOrderDocumentPrinter } from './purchase-order-document';
describe('Purchase orders', () => {
  let component: Purchases; let create: ReturnType<typeof vi.fn>; let list: ReturnType<typeof vi.fn>; let send: ReturnType<typeof vi.fn>; let print: ReturnType<typeof vi.fn>;
  const permissions = new Set(['PURCHASE_ORDERS.MANAGE', 'SUPPLIERS.READ', 'PRODUCTS.READ', 'INVENTORY.READ']);
  const supervisor = { id: 'operator-1', userName: 'supervisor', roles: ['SUPERVISOR'] };
  const session = {
    has: (permission: string) => permissions.has(permission),
    hasRole: (...codes: string[]) => supervisor.roles.some(role => codes.includes(role.trim().toUpperCase())),
    user: () => supervisor,
  };
  beforeEach(() => { create = vi.fn(); send = vi.fn(); print = vi.fn().mockResolvedValue(undefined); list = vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }); permissions.clear(); ['PURCHASE_ORDERS.MANAGE', 'SUPPLIERS.READ', 'PRODUCTS.READ', 'INVENTORY.READ'].forEach(x => permissions.add(x)); TestBed.configureTestingModule({ providers: [
    { provide: PurchasesApi, useValue: {} }, { provide: OrdersApi, useValue: { create, send, list } },
    { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
    { provide: PurchaseOrderDocumentPrinter, useValue: { print } },
    { provide: SessionService, useValue: session },
  ] }); component = TestBed.runInInjectionContext(() => new Purchases()); component.suppliers.set([{ id: 'supplier', name: 'Proveedor', isActive: true } as any]); component.orderSupplierId = 'supplier'; component.orderLines = [{ productId: 'product', productCode: 'P1', productName: 'Producto', available: 4, selected: true, quantity: 2 }]; });
  it('rechaza cantidades fraccionarias y productos sin seleccionar', async () => { component.orderLines[0].quantity = 1.5; expect(component.orderValidation()).toContain('enteras'); await component.saveOrder(); expect(create).not.toHaveBeenCalled(); component.orderLines[0].selected = false; expect(component.orderValidation()).toContain('Incluye'); });
  it('rechaza creación sin permiso efectivo', async () => { permissions.delete('PURCHASE_ORDERS.MANAGE'); await component.saveOrder(); expect(create).not.toHaveBeenCalled(); expect(component.saving()).toBe(false); });
  it('impide doble envío mientras espera al servidor', async () => {
    let resolve!: (value: { id: string; version: number; status: string }) => void;
    create.mockReturnValue(new Promise(r => { resolve = r; }));
    const first = component.saveOrder();
    expect(component.saving()).toBe(true);
    await component.saveOrder();
    expect(create).toHaveBeenCalledTimes(1);
    resolve({ id: 'order-1', version: 1, status: 'BORRADOR' });
    await first;
    expect(component.saving()).toBe(false);
  });
  it('genera el documento imprimible después de enviar el pedido', async () => {
    const draft = { id: 'order-1', supplierId: 'supplier', status: 'BORRADOR', visibleNumber: 'PED-1', createdAt: '2026-11-15T12:00:00Z', version: 1, sentAt: null, closedAt: null, lines: [{ id: 'line', productId: 'product', productCode: 'P1', productName: 'Producto', requestedQuantity: 2, receivedQuantity: 0, pendingQuantity: 2, excessQuantity: 0, suggestedQuantity: 2, estimatedUnitCost: null, observation: null }] } as any;
    const sentOrder = { ...draft, status: 'ENVIADO', version: 2, sentAt: '2026-11-15T12:01:00Z' };
    component.selectedOrder.set(draft); send.mockResolvedValue(sentOrder);
    await component.sendOrder();
    expect(send).toHaveBeenCalledWith(draft);
    expect(print).toHaveBeenCalledWith(sentOrder, expect.objectContaining({ id: 'supplier' }), '');
    expect(component.notice()).toContain('documento generado');
  });
});
