import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PurchasesApi, Purchase } from './purchases-api';
import { authorizationAttempt } from '../../core/api';
describe('PurchasesApi', () => {
  let api: PurchasesApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(PurchasesApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('filtra compras desde el servidor', async () => { const task = api.list({ supplierId: 's', status: 'BORRADOR', page: 3 }); const req = http.expectOne(r => r.url === '/api/purchases'); expect(req.request.params.get('supplierId')).toBe('s'); expect(req.request.params.get('page')).toBe('3'); req.flush({ items: [], page: 3, pageSize: 20, totalCount: 0, totalPages: 0 }); await task; });
  it('confirma con versión e idempotencia y propaga conflictos', async () => { const p = { id: 'p', version: 4 } as Purchase; const task = api.confirm(p, 'key-1'); const req = http.expectOne('/api/purchases/p/confirm'); expect(req.request.headers.get('Idempotency-Key')).toBe('key-1'); expect(req.request.body).toEqual({ expectedVersion: 4 }); req.flush({ title: 'Conflicto' }, { status: 409, statusText: 'Conflict' }); await expect(task).rejects.toMatchObject({ status: 409 }); });
  it('anula con motivo, versión, idempotencia y credenciales transitorias', async () => {
    const purchase = { id: 'p', version: 7 } as Purchase;
    const task = api.cancel(purchase, 'same-key', 'reason', 'Observación', 'admin', 'clave');
    const request = http.expectOne('/api/purchases/p/cancel');
    expect(request.request.headers.get('Idempotency-Key')).toBe('same-key');
    expect(request.request.context.get(authorizationAttempt)).toBe(true);
    expect(request.request.body).toEqual({ expectedVersion: 7, reasonId: 'reason', observation: 'Observación', authorization: { userName: 'admin', password: 'clave' } });
    request.flush({ ...purchase, status: 'ANULADA' });
    await task;
  });
});
