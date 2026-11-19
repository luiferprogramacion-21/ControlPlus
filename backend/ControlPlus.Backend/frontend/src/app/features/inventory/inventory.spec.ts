import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { Inventory } from './inventory';
import { InventoryApi, Stock } from './inventory-api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';

describe('Inventory', () => {
  let component: Inventory;
  const api = { stock: vi.fn(), movements: vi.fn(), reasons: vi.fn(), adjust: vi.fn() };
  const confirmAction = vi.fn();
  const dialog = { open: vi.fn(async (prompt: AuthorizationPrompt) => { try { await prompt.run({ userName: 'supervisor', password: 'clave' }); } catch { /* La ventana conserva el error. */ } return true; }) };
  const session = { has: (permission: string) => permission === 'INVENTORY.ADJUST', user: () => ({ userName: 'ejecutor' }) };
  const stock = { productId: 'p', name: 'Producto', currentStock: 8, reservedStock: 3, version: 2, isActive: true } as Stock;
  beforeEach(async () => { vi.clearAllMocks(); confirmAction.mockResolvedValue(true); TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction } }, { provide: InventoryApi, useValue: api }, { provide: SessionService, useValue: session }, { provide: AuthorizationDialog, useValue: dialog }] }); component = TestBed.runInInjectionContext(() => new Inventory()); api.stock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }); api.movements.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }); api.reasons.mockResolvedValue([]); await component.select(stock); });
  it('abre los movimientos en una ventana modal y permite cerrarla', async () => {
    const fixture = TestBed.createComponent(Inventory); fixture.detectChanges(); await fixture.whenStable();
    await fixture.componentInstance.openHistory(stock); fixture.detectChanges();
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
  it('abre un ajuste sin consultar el historial de movimientos', async () => {
    vi.clearAllMocks(); await component.openAdjustment(stock);
    expect(component.adjustmentOpen()).toBe(true); expect(component.adjustmentProductId()).toBe('p');
    expect(api.movements).not.toHaveBeenCalled();
  });
  it('detiene el reintento automático tras conflicto', async () => {
    component.quantityDelta = 1; component.reasonId = 'r';
    api.adjust.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await component.adjust(); expect(component.conflict()).toBe(true); expect(component.hasPending).toBe(false);
    await component.adjust(); expect(api.adjust).toHaveBeenCalledTimes(1);
  });
  it('muestra Disponible, Stock bajo, Agotado e Inactivo con el criterio del Dashboard', () => {
    const base = { isActive: true, minimumStock: 10 } as Stock;
    expect(component.stockStatus({ ...base, availableStock: 11 })).toEqual({ label: 'Disponible', tone: 'success' });
    expect(component.stockStatus({ ...base, availableStock: 10 })).toEqual({ label: 'Stock bajo', tone: 'warning' });
    expect(component.stockStatus({ ...base, availableStock: 0 })).toEqual({ label: 'Agotado', tone: 'error' });
    expect(component.stockStatus({ ...base, availableStock: 0, minimumStock: 0 })).toEqual({ label: 'Agotado', tone: 'error' });
    expect(component.stockStatus({ ...base, isActive: false, availableStock: 50 })).toEqual({ label: 'Inactivo', tone: 'neutral' });
  });
  it('ofrece el filtro de estado y solo muestra Inactivos con permiso', async () => {
    api.stock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    const fixture = TestBed.createComponent(Inventory); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const options = Array.from(fixture.nativeElement.querySelectorAll('#stock-status option') as NodeListOf<HTMLOptionElement>).map(o => o.textContent?.trim());
    expect(options).toEqual(['Todos los activos', 'Disponibles', 'Stock bajo', 'Agotados']);
    expect(fixture.nativeElement.textContent).not.toContain('Incluir inactivos');
    expect(api.stock).toHaveBeenCalledWith(expect.objectContaining({ status: '' }));
  });
  it('abre filtrado por stock bajo cuando llega desde el Dashboard', async () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [
      { provide: ConfirmationDialog, useValue: { confirm: confirmAction } },
      { provide: InventoryApi, useValue: api }, { provide: SessionService, useValue: session }, { provide: AuthorizationDialog, useValue: dialog },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ filtro: 'stock-bajo' }) } } },
    ] });
    api.stock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    const fixture = TestBed.createComponent(Inventory); fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.componentInstance.status).toBe('STOCK_BAJO');
    expect(api.stock).toHaveBeenCalledWith(expect.objectContaining({ status: 'STOCK_BAJO' }));
  });

  it('cancelar un descarte conserva producto y clave del ajuste incierto', async () => {
    await component.openAdjustment(stock); component.quantityDelta = 1; component.reasonId = 'r';
    api.adjust.mockRejectedValue(new HttpErrorResponse({ status: 0 })); await component.adjust();
    const key = api.adjust.mock.calls[0][1];
    expect(component.hasPending).toBe(true); confirmAction.mockResolvedValue(false);
    await component.closeAdjustment();
    expect(component.adjustmentOpen()).toBe(true); expect(component.hasPending).toBe(true);
    expect(await component.select({ ...stock, productId: 'otro' })).toBe(false);
    expect(component.selected()?.productId).toBe('p');
    await component.adjust(); expect(api.adjust.mock.calls[1][1]).toBe(key);
    confirmAction.mockResolvedValue(true); await component.closeAdjustment();
    expect(component.hasPending).toBe(false); expect(component.adjustmentOpen()).toBe(false);
  });
});
