import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Purchases } from './purchases';
import { OrdersApi, Order } from './orders-api';
import { PurchasesApi } from './purchases-api';
import { SuppliersApi } from '../suppliers/suppliers-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';

describe('Transiciones de pedidos', () => {
  let component: Purchases;
  let http: HttpTestingController;
  let permissions: Set<string>;
  let prompt: AuthorizationPrompt | null;
  const dialog = { open: vi.fn(async (value: AuthorizationPrompt) => { prompt = value; return false; }) };
  const order = (status: string, received = 0): Order => ({ id: 'order', supplierId: 'supplier', status, visibleNumber: 'PED-1', createdAt: '2026-10-25', version: 4, sentAt: null, closedAt: null, lines: [{ id: 'line', productId: 'product', productCode: 'P1', productName: 'Producto', requestedQuantity: 5, receivedQuantity: received, pendingQuantity: 5 - received, excessQuantity: 0, suggestedQuantity: 0, estimatedUnitCost: 100, observation: null }] });

  beforeEach(() => {
    prompt = null; dialog.open.mockClear();
    permissions = new Set(['PURCHASE_ORDERS.MANAGE', 'PURCHASE_ORDERS.CANCEL', 'PURCHASES.RECEIVE']);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), OrdersApi,
      { provide: PurchasesApi, useValue: {} }, { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: dialog },
      { provide: SessionService, useValue: { has: (code: string) => permissions.has(code), hasRole: (...codes: string[]) => codes.includes('ADMINISTRADOR'), user: () => ({ roles: ['ADMINISTRADOR'], userName: 'operador' }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Purchases());
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('restringe cierre y anulación según estado, permisos y cantidades recibidas', () => {
    component.selectedOrder.set(order('BORRADOR'));
    expect(component.canStartOrderClose).toBe(false);
    expect(component.canStartOrderCancel).toBe(true);
    component.selectedOrder.set(order('ENVIADO'));
    expect(component.canStartOrderClose).toBe(true);
    component.selectedOrder.set(order('RECIBIDO_PARCIAL', 2));
    expect(component.canStartOrderClose).toBe(true);
    expect(component.canStartOrderCancel).toBe(false);
    permissions.delete('PURCHASE_ORDERS.CANCEL');
    component.selectedOrder.set(order('BORRADOR'));
    expect(component.canStartOrderCancel).toBe(false);
  });

  it('cancelar autorización no escribe; exige motivo y observación para OTRO', async () => {
    component.selectedOrder.set(order('ENVIADO'));
    const starting = component.startOrderTransition('close');
    http.expectOne('/api/purchase-orders/reasons/close').flush([{ id: 'reason', code: 'OTRO', name: 'Otro', displayOrder: 3 }]);
    await starting;
    component.orderReasonId = 'reason';
    await component.submitOrderTransition();
    expect(component.error()).toContain('observación');
    expect(dialog.open).not.toHaveBeenCalled();
    component.orderObservation = 'Proveedor no entregará';
    await component.submitOrderTransition();
    expect(prompt?.detail).toContain('PED-1');
    http.expectNone('/api/purchase-orders/order/close');
  });

  it('envía credenciales solo tras autorizar; bloquea reenvío y conserva datos en conflicto', async () => {
    component.selectedOrder.set(order('ENVIADO'));
    const starting = component.startOrderTransition('close');
    http.expectOne('/api/purchase-orders/reasons/close').flush([{ id: 'reason', code: 'PENDIENTE_NO_REQUERIDO', name: 'Pendiente', displayOrder: 1 }]);
    await starting;
    component.orderReasonId = 'reason'; component.orderObservation = 'Sin entrega';
    await component.submitOrderTransition();
    const first = prompt!.run({ userName: 'admin', password: 'secreto' });
    const request = http.expectOne('/api/purchase-orders/order/close');
    expect(request.request.body).toMatchObject({ expectedVersion: 4, reasonId: 'reason', observation: 'Sin entrega', authorization: { userName: 'admin', password: 'secreto' } });
    await expect(prompt!.run({ userName: 'admin', password: 'secreto' })).rejects.toThrow();
    http.expectNone('/api/purchase-orders/order/close');
    request.flush({ title: 'Conflicto' }, { status: 409, statusText: 'Conflict' });
    await expect(first).rejects.toBeTruthy();
    expect(component.orderConflict()).toBe(true);
    expect(component.orderObservation).toBe('Sin entrega');
    const refreshing = component.refreshOrder();
    http.expectOne('/api/purchase-orders/order').flush({ ...order('ENVIADO'), version: 5 });
    await refreshing;
    expect(component.orderConflict()).toBe(false);
  });
});
