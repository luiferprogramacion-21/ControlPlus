import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { InventoryApi } from './inventory-api';

describe('InventoryApi', () => {
  let api: InventoryApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(InventoryApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('consulta existencias paginadas', async () => {
    const task = api.stock({ search: 'ABC', includeInactive: false, page: 3, pageSize: 20 });
    const req = http.expectOne(r => r.url === '/api/inventory/stock' && r.params.get('search') === 'ABC' && r.params.get('page') === '3');
    req.flush({ items: [], page: 3, pageSize: 20, totalCount: 0, totalPages: 0 }); expect((await task).items).toEqual([]);
  });
  it('envía versión y clave idempotente en un ajuste', async () => {
    const input = { productId: 'p', quantityDelta: -2, reasonId: 'r', expectedVersion: 7, observation: null, authorization: { userName: 'supervisor', password: 'secreto' } };
    const task = api.adjust(input, 'key-1'); const req = http.expectOne('/api/inventory/adjustments');
    expect(req.request.headers.get('Idempotency-Key')).toBe('key-1'); expect(req.request.body).toEqual(input); req.flush({ id: 'm' }); await task;
  });
});
