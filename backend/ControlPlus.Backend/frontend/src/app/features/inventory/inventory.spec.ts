import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Inventory } from './inventory';
import { InventoryApi, Stock } from './inventory-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';

describe('Inventory', () => {
  let component: Inventory;
  const api = { stock: vi.fn(), movements: vi.fn(), reasons: vi.fn(), adjust: vi.fn() };
  const dialog = { open: vi.fn(async (prompt: AuthorizationPrompt) => { try { await prompt.run({ userName: 'supervisor', password: 'clave' }); } catch { /* La ventana conserva el error. */ } return true; }) };
  const session = { has: (permission: string) => permission === 'INVENTORY.ADJUST', user: () => ({ userName: 'ejecutor' }) };
  const stock = { productId: 'p', name: 'Producto', currentStock: 8, reservedStock: 3, version: 2, isActive: true } as Stock;
  beforeEach(() => { vi.clearAllMocks(); TestBed.configureTestingModule({ providers: [{ provide: InventoryApi, useValue: api }, { provide: SessionService, useValue: session }, { provide: AuthorizationDialog, useValue: dialog }] }); component = TestBed.runInInjectionContext(() => new Inventory()); api.stock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }); api.movements.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }); api.reasons.mockResolvedValue([]); component.select(stock); });
  it('abre los movimientos en una ventana modal y permite cerrarla', async () => {
    const fixture = TestBed.createComponent(Inventory); fixture.detectChanges(); await fixture.whenStable();
    fixture.componentInstance.openHistory(stock); fixture.detectChanges();
    const modal = fixture.nativeElement.querySelector('[role="dialog"][aria-labelledby="inventory-history-title"]');
    expect(modal).toBeTruthy(); expect(modal.textContent).toContain('Producto');
    expect(fixture.nativeElement.querySelector('section.inventory-history')).toBeNull();
    modal.querySelector('.module-form-sheet__backdrop').click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#inventory-history-title')).toBeNull();
  });
  it('rechaza una salida que invade la reserva', async () => {
    component.quantityDelta = -6; component.reasonId = 'r';
    await component.adjust(); expect(api.adjust).not.toHaveBeenCalled();
  });
  it('abre un ajuste sin consultar el historial de movimientos', () => {
    vi.clearAllMocks(); component.openAdjustment(stock);
    expect(component.adjustmentOpen()).toBe(true); expect(component.adjustmentProductId()).toBe('p');
    expect(api.movements).not.toHaveBeenCalled();
  });
  it('detiene el reintento automático tras conflicto', async () => {
    component.quantityDelta = 1; component.reasonId = 'r';
    api.adjust.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await component.adjust(); expect(component.conflict()).toBe(true); expect(component.hasPending).toBe(false);
    await component.adjust(); expect(api.adjust).toHaveBeenCalledTimes(1);
  });
});
