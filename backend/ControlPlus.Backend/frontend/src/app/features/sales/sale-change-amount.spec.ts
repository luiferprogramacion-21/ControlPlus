import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog } from '../../shared/authorization-dialog';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { ChangePreparation, SaleDetail, SalesApi } from './sales-api';
import { SaleChange } from './sale-change';

const sale: SaleDetail = { id: 's1', visibleNumber: 'VTA-1', occurredAtUtc: '', operatingDate: '', grossSubtotal: 30000, discountTotal: 0, total: 30000, change: 0, state: 'CONFIRMADA', version: 4, creditId: null,
  payments: [{ id: 'p', methodCode: 'EFECTIVO', methodName: 'Efectivo', appliedAmount: 30000, receivedAmount: 30000, change: 0, reference: null }],
  lines: [{ id: 'l1', productId: 'p1', productCode: 'P-1', productName: 'Camisa', measurementUnit: 'UND', quantity: 3, priceType: 'MINORISTA', unitPrice: 10000, discountAmount: 0, grossAmount: 30000, netAmount: 30000 }] };
const prep: ChangePreparation = { saleId: 's1', visibleNumber: 'VTA-1', saleType: 'CONTADO', saleState: 'CONFIRMADA', saleVersion: 4, operatingDate: '', changeDeadline: '2026-11-30', currentLocalDate: '2026-11-26',
  withinDeadline: true, canStartChange: true, creditId: null, creditVersion: null, creditBalance: null,
  lines: [{ saleLineId: 'l1', productId: 'p1', productCode: 'P-1', productName: 'Camisa', soldQuantity: 3, returnedQuantity: 0, remainingQuantity: 3 }] };
const bag = { id: 'p2', name: 'Bolso', internalCode: 'P-2', retailPrice: 50000, wholesalePrice: null, availableStock: 5, isActive: true } as unknown as Product;

describe('SaleChange: el valor a cobrar o devolver se ve en la casilla (pruebas del usuario del 26/11/2026)', () => {
  it('actualiza el campo al cambiar la cantidad entregada', async () => {
    TestBed.configureTestingModule({ providers: [
      { provide: SalesApi, useValue: { changePreparation: vi.fn().mockResolvedValue(prep), pricingRules: vi.fn().mockResolvedValue({ wholesaleMinimumQuantity: 12 }) } },
      { provide: CatalogApi, useValue: {} }, { provide: AuthorizationDialog, useValue: {} },
      { provide: SessionService, useValue: { user: () => ({ userName: 'admin' }), hasRole: () => true } }] });
    const fixture = TestBed.createComponent(SaleChange);
    fixture.componentRef.setInput('sale', sale); fixture.componentRef.setInput('mode', 'exchange');
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const c = fixture.componentInstance;
    await vi.waitFor(() => expect(c.returning().length).toBe(1));
    c.toggleReturn(c.returning()[0], true); c.returning()[0].quantity = 1; fixture.detectChanges(); await fixture.whenStable();
    c.addDelivered(bag); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    const amount = () => (fixture.nativeElement.querySelector('#change-amount-0') as HTMLInputElement | null)?.value;
    expect(amount()).toBe('40.000');
    const qty = fixture.nativeElement.querySelector('input[aria-label="Cantidad entregada de Bolso"]') as HTMLInputElement;
    qty.value = '2'; qty.dispatchEvent(new InputEvent('input', { inputType: 'insertText' }));
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); await fixture.whenStable();
    expect(c.difference).toBe(90000);
    expect(amount()).toBe('90.000');
  });

  it('en una devolución propone en la casilla el valor y el medio con que se pagó', async () => {
    TestBed.configureTestingModule({ providers: [
      { provide: SalesApi, useValue: { changePreparation: vi.fn().mockResolvedValue(prep), pricingRules: vi.fn().mockResolvedValue({ wholesaleMinimumQuantity: 12 }) } },
      { provide: CatalogApi, useValue: {} }, { provide: AuthorizationDialog, useValue: {} },
      { provide: SessionService, useValue: { user: () => ({ userName: 'admin' }), hasRole: () => true } }] });
    const fixture = TestBed.createComponent(SaleChange);
    fixture.componentRef.setInput('sale', { ...sale, payments: [{ ...sale.payments[0], methodCode: 'NEQUI', methodName: 'Nequi', receivedAmount: null }] });
    fixture.componentRef.setInput('mode', 'remove');
    fixture.detectChanges(); await fixture.whenStable();
    const c = fixture.componentInstance;
    await vi.waitFor(() => expect(c.returning().length).toBe(1));
    c.toggleReturn(c.returning()[0], true); c.returning()[0].quantity = 2;
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); await fixture.whenStable();
    expect((fixture.nativeElement.querySelector('#change-amount-0') as HTMLInputElement).value).toBe('20.000');
    expect((fixture.nativeElement.querySelector('#change-method-0') as HTMLSelectElement).value).toBe('NEQUI');
  });
});
