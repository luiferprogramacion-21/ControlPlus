import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Layaway, LayawaysApi } from './layaways-api';

describe('LayawaysApi: apartados', () => {
  let api: LayawaysApi;
  let http: HttpTestingController;
  const layaway = { id: 'a1', version: 4 } as Layaway;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(LayawaysApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lista con filtros y omite los vacíos; overdue falso se envía', async () => {
    const task = api.list({ page: 2, pageSize: 20, state: 'ACTIVO', overdue: false, dueFrom: '2026-11-01' });
    const request = http.expectOne(r => r.url === '/api/layaways');
    expect(request.request.params.get('state')).toBe('ACTIVO');
    expect(request.request.params.get('overdue')).toBe('false');
    expect(request.request.params.get('dueFrom')).toBe('2026-11-01');
    expect(request.request.params.has('customerId')).toBe(false);
    expect(request.request.params.has('dueTo')).toBe(false);
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 });
    await task;
  });

  it('omite overdue cuando no se filtra', async () => {
    const task = api.list({ page: 1, pageSize: 20 });
    const request = http.expectOne(r => r.url === '/api/layaways');
    expect(request.request.params.has('overdue')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    await task;
  });

  it('consulta el detalle', async () => {
    const task = api.get('a1');
    http.expectOne('/api/layaways/a1').flush({ layaway, lines: [], payments: [], delivery: null, cancellation: null, initialMinimumPercentage: 20 });
    expect((await task).initialMinimumPercentage).toBe(20);
  });

  it('abona con expectedVersion, medios y clave de idempotencia', async () => {
    const payments = [{ methodCode: 'EFECTIVO' as const, appliedAmount: 5000, receivedAmount: 10000, reference: null }];
    const task = api.applyPayment(layaway, payments, 'clave-1');
    const request = http.expectOne('/api/layaways/a1/payments');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('clave-1');
    expect(request.request.body).toEqual({ expectedVersion: 4, payments });
    request.flush({ layawayId: 'a1', balance: 0, state: 'PAGADO', version: 5, correlationId: 'c', change: 5000, payments: [] });
    expect((await task).state).toBe('PAGADO');
  });

  it('entrega con expectedVersion y clave de idempotencia, sin otros campos', async () => {
    const task = api.deliver(layaway, 'clave-2');
    const request = http.expectOne('/api/layaways/a1/delivery');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toBe('clave-2');
    expect(request.request.body).toEqual({ expectedVersion: 4 });
    request.flush({ layawayId: 'a1', number: 'APT-1', state: 'ENTREGADO', version: 5, deliveredAtUtc: '', deliveredByUserId: 'u', correlationId: 'c', products: [] });
    expect((await task).state).toBe('ENTREGADO');
  });
});
