import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Credit, CreditsApi } from './credits-api';

describe('CreditsApi', () => {
  let api: CreditsApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(CreditsApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('envía los filtros y la paginación compatibles con la API', async () => {
    const task = api.list({ page: 2, pageSize: 10, state: 'VENCIDO', dueFrom: '2026-10-01', dueTo: '2026-10-31', customerId: 'customer-id', saleNumber: 'VTA-1' });
    const req = http.expectOne(r => r.url === '/api/credits');
    expect(req.request.params.get('page')).toBe('2'); expect(req.request.params.get('pageSize')).toBe('10');
    expect(req.request.params.get('state')).toBe('VENCIDO'); expect(req.request.params.get('customerId')).toBe('customer-id');
    expect(req.request.params.get('dueFrom')).toBe('2026-10-01'); expect(req.request.params.get('dueTo')).toBe('2026-10-31'); expect(req.request.params.get('saleNumber')).toBe('VTA-1');
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 }); await task;
  });
  it('consulta detalle e historial paginado por separado', async () => {
    const detail = api.get('credit-id'); http.expectOne('/api/credits/credit-id').flush({ id: 'credit-id' }); await detail;
    const history = api.payments('credit-id', 3, 20); const req = http.expectOne(r => r.url === '/api/credits/credit-id/payments');
    expect(req.request.params.get('page')).toBe('3'); expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({ items: [], page: 3, pageSize: 20, totalCount: 0, totalPages: 0 }); await history;
  });
  it('envía versión, pagos combinados y clave de idempotencia', async () => {
    const credit = { id: 'credit-id', version: 7 } as Credit;
    const payments = [{ methodCode: 'EFECTIVO' as const, appliedAmount: 3000, receivedAmount: 5000, reference: null }, { methodCode: 'NEQUI' as const, appliedAmount: 2000, receivedAmount: null, reference: 'NQ-42' }];
    const task = api.applyPayment(credit, payments, 'key-123');
    const req = http.expectOne('/api/credits/credit-id/payments');
    expect(req.request.method).toBe('POST'); expect(req.request.headers.get('Idempotency-Key')).toBe('key-123');
    expect(req.request.body).toEqual({ expectedVersion: 7, payments });
    req.flush({ creditId: 'credit-id', balance: 0, state: 'PAGADO', version: 8, correlationId: 'correlation', payments: [] });
    expect((await task).balance).toBe(0);
  });
  it('consulta motivos y anula un abono con versión, autorización e idempotencia', async () => {
    const credit = { id: 'credit-id', version: 8 } as Credit;
    const reasons = api.paymentCancellationReasons();
    http.expectOne('/api/credits/payment-cancellation-reasons').flush([{ id: 'reason', code: 'ERROR_REGISTRO', name: 'Error', displayOrder: 10 }]);
    expect((await reasons)[0].code).toBe('ERROR_REGISTRO');
    const task = api.cancelPayment(credit, 'payment-id', { expectedVersion: 8, reasonId: 'reason', observation: null, authorization: { userName: 'admin', password: 'secret' } }, 'cancel-key');
    const req = http.expectOne('/api/credits/credit-id/payments/payment-id/cancel');
    expect(req.request.method).toBe('POST'); expect(req.request.headers.get('Idempotency-Key')).toBe('cancel-key');
    expect(req.request.body).toEqual({ expectedVersion: 8, reasonId: 'reason', observation: null, authorization: { userName: 'admin', password: 'secret' } });
    req.flush({ creditId: 'credit-id', balance: 5000, state: 'PENDIENTE', version: 9, cancelledPaymentIds: ['payment-id'], correlationId: 'correlation', cancelledAtUtc: '2026-11-16T00:00:00Z', reversedAmount: 1000 });
    expect((await task).reversedAmount).toBe(1000);
  });
  it('condona con motivo, versión e idempotencia', async () => {
    const credit = { id: 'credit-id', version: 9 } as Credit;
    const task = api.forgive(credit, 'Acuerdo administrativo', 'forgive-key');
    const req = http.expectOne('/api/credits/credit-id/forgive');
    expect(req.request.headers.get('Idempotency-Key')).toBe('forgive-key');
    expect(req.request.body).toEqual({ expectedVersion: 9, reason: 'Acuerdo administrativo' });
    req.flush({ creditId: 'credit-id', balance: 0, state: 'ANULADO', version: 10, authorizationId: 'authorization', correlationId: 'correlation', forgivenAtUtc: '2026-11-16T00:00:00Z' });
    expect((await task).state).toBe('ANULADO');
  });
});
