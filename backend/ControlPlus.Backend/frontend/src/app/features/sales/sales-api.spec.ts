import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesApi, SaleInput } from './sales-api';

describe('SalesApi: venta a crédito', () => {
  let api: SalesApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(SalesApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('usa la confirmación de ventas con vencimiento, autorizador y sin pago inicial', async () => {
    const input: SaleInput = { customerId: 'customer-id', lines: [{ productId: 'product-id', quantity: 1, priceType: 'MINORISTA', discount: null }], generalDiscount: null, discountReason: null, payments: [], saleType: 'CREDITO', dueDate: '2026-11-08', authorization: { userName: 'supervisor', password: 'secreto-de-prueba' } };
    const task = api.confirm(input, 'sale-key');
    const request = http.expectOne('/api/sales');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('sale-key');
    expect(request.request.body).toEqual(input);
    request.flush({ id: 'sale-id', visibleNumber: 'VTA-1', total: 10000, change: 0, state: 'CONFIRMADA', payments: [], creditId: 'credit-id' });
    expect((await task).creditId).toBe('credit-id');
  });
});
