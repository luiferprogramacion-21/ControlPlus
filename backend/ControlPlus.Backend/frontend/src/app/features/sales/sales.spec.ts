import { TestBed } from '@angular/core/testing';
import { convertToParamMap, ActivatedRoute } from '@angular/router';
import { vi } from 'vitest';
import { Sales } from './sales';
import { SalesApi } from './sales-api';
import { CashApi } from '../cash/cash-api';
import { CatalogApi } from '../catalog/catalog-api';
import { SessionService } from '../../core/session.service';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';

const product = { id: '019a0000-0000-7000-8000-000000000001', isActive: true, availableStock: 5, retailPrice: 10000, wholesalePrice: null } as any;
const customer = { id: '019a0000-0000-7000-8000-000000000002', fullName: 'Ana', isActive: true, phone: '3001234567', documentType: null, documentNumber: null } as any;
const cash = { hasOpenShift: true, requiresPreviousShiftClosure: false, requiresOperatorSession: false, currentShift: { operatingDate: '2026-10-24', responsibleUserId: '019a0000-0000-7000-8000-000000000003' } } as any;

describe('Sales: crédito desde ambos accesos', () => {
  let component: Sales;
  const confirm = vi.fn();
  const state = vi.fn();
  let credentials = { userName: 'supervisor', password: 'clave-de-prueba' };
  const dialog = { open: vi.fn(async (prompt: AuthorizationPrompt) => { try { await prompt.run(credentials); } catch { /* La ventana muestra el rechazo. */ } return true; }) };
  beforeEach(() => {
    vi.clearAllMocks(); credentials = { userName: 'supervisor', password: 'clave-de-prueba' }; state.mockResolvedValue(cash);
    TestBed.configureTestingModule({ providers: [
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ modalidad: 'credito' }) } } },
      { provide: SalesApi, useValue: { confirm } }, { provide: CashApi, useValue: { state } }, { provide: CatalogApi, useValue: {} },
      { provide: AuthorizationDialog, useValue: dialog },
      { provide: SessionService, useValue: { has: (permission: string) => ['SALES.CREATE', 'CREDIT.REQUEST'].includes(permission), hasRole: (...codes: string[]) => codes.includes('CAJERO'), user: () => ({ id: '019a0000-0000-7000-8000-000000000003', userName: 'cajero', roles: ['CAJERO'] }) } },
    ] });
    component = TestBed.runInInjectionContext(() => new Sales());
    component.ngOnInit(); component.cashState.set(cash);
    component.add(product); component.customers.set([customer]); component.customerId = customer.id;
    component.dueDate = '2026-11-08';
  });
  it('entra desde Cartera en modalidad crédito y exige cliente', () => {
    expect(component.saleType()).toBe('CREDITO');
    component.startCheckout(); expect(component.checkoutMode()).toBe(true);
    component.customerId = '';
    expect(component.canConfirm).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
  });
  it('entra desde Punto de ventas en la misma modalidad crédito', () => {
    component.startCashSale();
    expect(component.saleType()).toBe('CONTADO');
    component.startCreditSale();
    expect(component.saleType()).toBe('CREDITO');
    expect(component.checkoutMode()).toBe(true);
  });
  it('confirma una sola venta a crédito sin pagos y conserva clave ante reintento', async () => {
    let rejectFirst!: (error: Error) => void;
    const firstResponse = new Promise<never>((_, reject) => { rejectFirst = reject; });
    confirm.mockReturnValueOnce(firstResponse).mockResolvedValueOnce({ id: 'sale-id', visibleNumber: 'VTA-1', total: 10000, change: 0, state: 'CONFIRMADA', payments: [], creditId: 'credit-id' });
    component.startCheckout();
    expect(component.canConfirm).toBe(true);
    const first = component.confirm();
    const duplicate = component.confirm();
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(confirm.mock.calls[0][0]).toEqual(expect.objectContaining({ saleType: 'CREDITO', customerId: customer.id, dueDate: '2026-11-08', payments: [], authorization: { userName: 'supervisor', password: 'clave-de-prueba' } }));
    const key = confirm.mock.calls[0][1];
    rejectFirst(new Error('sin respuesta'));
    await Promise.all([first, duplicate]);
    expect(component.sale()).toBeNull();
    expect(component.locked).toBe(true);
    await component.confirm();
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(confirm.mock.calls[1][1]).toBe(key);
    expect(component.sale()?.creditId).toBe('credit-id');
  });
  it('permite corregir la autorización rechazada sin repetir la venta', async () => {
    confirm.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 })).mockResolvedValueOnce({ id: 'sale-id', visibleNumber: 'VTA-1', total: 10000, change: 0, state: 'CONFIRMADA', payments: [], creditId: 'credit-id' });
    component.startCheckout();
    expect(component.canConfirm).toBe(true);
    await component.confirm();
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(component.sale()).toBeNull();
    expect(component.locked).toBe(false);
    expect(confirm).toHaveBeenCalledTimes(1);
    const rejectedKey = confirm.mock.calls[0][1];
    credentials = { userName: 'supervisor', password: 'clave-corregida' };
    await component.confirm();
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(confirm.mock.calls[1][0]).toEqual(expect.objectContaining({ payments: [], authorization: { userName: 'supervisor', password: 'clave-corregida' } }));
    expect(confirm.mock.calls[1][1]).not.toBe(rejectedKey);
    expect(component.sale()?.creditId).toBe('credit-id');
  });
  it('no cambia el autorizador en un reintento de respuesta incierta', async () => {
    confirm.mockRejectedValueOnce(new Error('sin respuesta'));
    component.startCheckout();
    await component.confirm();
    const key = confirm.mock.calls[0][1];
    credentials = { userName: 'otro-admin', password: 'otra-clave' };
    await component.confirm();
    expect(confirm).toHaveBeenCalledOnce();
    credentials = { userName: 'supervisor', password: 'clave-nueva' };
    confirm.mockResolvedValueOnce({ id: 'sale-id', visibleNumber: 'VTA-1', total: 10000, change: 0, state: 'CONFIRMADA', payments: [], creditId: 'credit-id' });
    await component.confirm();
    expect(confirm.mock.calls[1][1]).toBe(key);
  });
});
