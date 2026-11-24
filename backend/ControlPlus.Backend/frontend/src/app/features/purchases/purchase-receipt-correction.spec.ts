import { TestBed } from '@angular/core/testing';
import { Purchases } from './purchases';
import { Purchase, PurchaseCorrectionPreparation, PurchasesApi } from './purchases-api';
import { OrdersApi } from './orders-api';
import { SuppliersApi } from '../suppliers/suppliers-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';

describe('Corrección de recepción de compra', () => {
  let component: Purchases;
  let prompt: AuthorizationPrompt | null;
  let roles = ['ADMINISTRADOR']; let permissions = ['PURCHASES.CANCEL', 'PURCHASES.RECEIVE'];
  const purchase = { id: 'purchase', orderId: 'order', version: 8, status: 'CONFIRMADA', visibleNumber: 'CMP-8' } as Purchase;
  const preparation: PurchaseCorrectionPreparation = { purchaseId: 'purchase', purchaseVersion: 8, orderId: 'order', orderVersion: 5, canReverse: true, blockReason: null, unitsToRemove: 3, originalTotal: 4200 };
  const api = { correctionPreparation: vi.fn(), reasons: vi.fn(), correctReceipt: vi.fn(), list: vi.fn() };
  const dialog = { open: vi.fn(async (value: AuthorizationPrompt) => { prompt = value; return false; }) };

  beforeEach(() => {
    vi.clearAllMocks(); prompt = null; roles = ['ADMINISTRADOR']; permissions = ['PURCHASES.CANCEL', 'PURCHASES.RECEIVE'];
    api.correctionPreparation.mockResolvedValue(preparation);
    api.reasons.mockResolvedValue([{ id: 'reason', code: 'ERROR_REGISTRO', name: 'Error de registro', description: null, displayOrder: 1 }]);
    api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    TestBed.configureTestingModule({ providers: [
      { provide: PurchasesApi, useValue: api }, { provide: OrdersApi, useValue: {} },
      { provide: SuppliersApi, useValue: {} }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: dialog },
      { provide: SessionService, useValue: { has: (code: string) => permissions.includes(code), hasRole: (...codes: string[]) => codes.some(code => roles.includes(code)), user: () => ({ userName: 'operador', roles }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Purchases());
    component.selectedPurchase.set(purchase);
  });

  it('muestra la explicación cuando hay movimientos posteriores y no solicita autorización', async () => {
    api.correctionPreparation.mockResolvedValueOnce({ ...preparation, canReverse: false, blockReason: 'Estos productos ya tuvieron movimientos posteriores.' });
    await component.startReceiptCorrection();
    expect(component.correctingReceipt()).toBe(true);
    expect(component.correctionPreparation()?.blockReason).toContain('movimientos posteriores');
    await component.submitReceiptCorrection();
    expect(dialog.open).not.toHaveBeenCalled();
    expect(api.correctReceipt).not.toHaveBeenCalled();
  });

  it('resume el antes y después, pide autorización y conserva las versiones preparadas', async () => {
    await component.startReceiptCorrection();
    component.correctionReasonId = 'reason';
    component.correctionObservation = 'Recepción duplicada';
    await component.submitReceiptCorrection();
    expect(prompt?.description).toContain('3 unidades');
    api.correctReceipt.mockResolvedValue({ ...purchase, status: 'ANULADA', version: 9 });
    await prompt!.run({ userName: 'admin', password: 'clave' });
    expect(api.correctReceipt).toHaveBeenCalledWith(purchase, preparation, expect.any(String), 'reason', 'Recepción duplicada', 'admin', 'clave');
    expect(component.selectedPurchase()?.status).toBe('ANULADA');
    expect(component.correctingReceipt()).toBe(false);
  });

  it('el Supervisor corrige la recepción con la autorización del Administrador (S.20)', async () => {
    roles = ['SUPERVISOR']; permissions = ['PURCHASES.RECEIVE'];
    expect(component.canCancel).toBe(false);
    expect(component.canCorrectReceipt).toBe(true);
    api.correctionPreparation.mockResolvedValueOnce({ ...preparation, requiresAdministratorAuthorization: true });
    await component.startReceiptCorrection();
    component.correctionReasonId = 'reason';
    await component.submitReceiptCorrection();
    expect(prompt?.description).toContain('autorización del Administrador');
    expect(prompt?.requireDifferentUser).toBe(true);
    roles = ['CAJERO']; permissions = [];
    expect(component.canCorrectReceipt).toBe(false);
  });
});
