import { TestBed } from '@angular/core/testing';
import { Purchases } from './purchases';
import { PurchasesApi, Purchase } from './purchases-api';
import { OrdersApi } from './orders-api';
import { SuppliersApi } from '../suppliers/suppliers-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';

describe('Anulación de compra con autorización', () => {
  let component: Purchases;
  let prompt: AuthorizationPrompt | null;
  const api = { cancel: vi.fn(), list: vi.fn() };
  const dialog = { open: vi.fn(async (value: AuthorizationPrompt) => { prompt = value; return false; }) };
  const purchase = { id: 'purchase', version: 5, status: 'CONFIRMADA', visibleNumber: 'CMP-1' } as Purchase;
  beforeEach(() => {
    vi.clearAllMocks(); prompt = null;
    api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: api }, { provide: OrdersApi, useValue: {} },
      { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: dialog },
      { provide: SessionService, useValue: { has: (code: string) => code === 'PURCHASES.CANCEL' || code === 'PURCHASES.RECEIVE', hasRole: (...codes: string[]) => codes.includes('ADMINISTRADOR'), user: () => ({ userName: 'operador', roles: ['ADMINISTRADOR'] }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Purchases());
    component.selectedPurchase.set(purchase);
    component.cancelReasonId = 'reason';
    component.reasons.set([{ id: 'reason', code: 'ERROR_REGISTRO', name: 'Error', description: null, displayOrder: 1 }]);
  });

  it('cancelar la ventana no invoca la API; motivo y versión siguen disponibles', async () => {
    await component.cancelPurchase();
    expect(prompt?.detail).toContain('CMP-1');
    expect(api.cancel).not.toHaveBeenCalled();
    expect(component.cancelReasonId).toBe('reason');
    expect(component.selectedPurchase()?.version).toBe(5);
  });

  it('bloquea doble envío y conserva clave y datos operativos tras respuesta incierta', async () => {
    await component.cancelPurchase();
    let rejectFirst!: (reason: Error) => void;
    api.cancel.mockReturnValueOnce(new Promise((_, reject) => { rejectFirst = reject; })).mockResolvedValueOnce({ ...purchase, status: 'ANULADA', version: 6 });
    const first = prompt!.run({ userName: 'admin', password: 'clave-1' });
    expect(component.saving()).toBe(true);
    await expect(prompt!.run({ userName: 'admin', password: 'clave-1' })).rejects.toThrow();
    expect(api.cancel).toHaveBeenCalledOnce();
    const key = api.cancel.mock.calls[0][1];
    rejectFirst(new Error('Respuesta perdida'));
    await expect(first).rejects.toBeTruthy();
    expect(component.cancelReasonId).toBe('reason');
    await prompt!.run({ userName: 'admin', password: 'clave-2' });
    expect(api.cancel.mock.calls[1][1]).toBe(key);
    expect(api.cancel.mock.calls[1][5]).toBe('clave-2');
    expect(component.selectedPurchase()?.status).toBe('ANULADA');
  });
});
