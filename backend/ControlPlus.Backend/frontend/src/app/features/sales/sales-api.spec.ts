import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesApi, SaleInput, printJobStatusText } from './sales-api';

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

describe('SalesApi: impresión', () => {
  let api: SalesApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(SalesApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('consulta los trabajos de una venta paginados', async () => {
    const task = api.printJobs('s1', 2, 10);
    const request = http.expectOne(r => r.url === '/api/sales/s1/print-jobs');
    expect(request.request.params.get('page')).toBe('2'); expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 });
    expect((await task).page).toBe(2);
  });
  it('solicita reimpresión sin cuerpo y con Idempotency-Key', async () => {
    const task = api.requestReprint('s1', 'k1');
    const request = http.expectOne('/api/sales/s1/reprint-requests');
    expect(request.request.method).toBe('POST'); expect(request.request.body).toBeNull(); expect(request.request.headers.get('Idempotency-Key')).toBe('k1');
    request.flush({ requestId: 'r', saleId: 's1', requestedAtUtc: '', status: 'SOLICITADA' });
    expect((await task).status).toBe('SOLICITADA');
  });
  it('crea perfiles, reintenta trabajos y lista la cola', async () => {
    const createInput = { name: 'Recibos', systemQueueName: 'COLA', columns: 32, printerType: 'RECIBOS' as const, labelWidthMm: null, labelHeightMm: null, resolutionDpi: null };
    const create = api.createPrintProfile(createInput);
    const post = http.expectOne('/api/printing/profiles'); expect(post.request.method).toBe('POST'); expect(post.request.body).toEqual(createInput);
    post.flush({ id: 'p', name: 'Recibos', systemQueueName: 'COLA', columns: 32, isDefault: true, active: true, version: 1 }); await create;
    const retry = api.retryPrintJob('s1', 'j1'); http.expectOne('/api/sales/s1/print-jobs/j1/retry').flush({ id: 'j1' }); await retry;
    const queue = api.printQueue(); http.expectOne(r => r.url === '/api/printing/jobs').flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }); await queue;
  });
  it('no presenta IMPRESO como salida física y distingue el resultado incierto', () => {
    expect(printJobStatusText({ status: 'IMPRESO', error: null })).toContain('cola de Windows');
    expect(printJobStatusText({ status: 'IMPRESO', error: null })).not.toMatch(/papel|físic/i);
    expect(printJobStatusText({ status: 'IMPRIMIENDO', error: null })).toBe('Envío en curso.');
    expect(printJobStatusText({ status: 'IMPRIMIENDO', error: 'timeout' })).toContain('Resultado incierto');
  });
});
