import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProductLabelsApi } from './product-labels-api';

describe('ProductLabelsApi', () => {
  let api: ProductLabelsApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(ProductLabelsApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());

  it('consulta productos y perfiles por las rutas protegidas de etiquetas', async () => {
    const profiles = api.profiles(); http.expectOne('/api/product-labels/profiles').flush([]); await profiles;
    const products = api.products('77001', 2, 10);
    const request = http.expectOne(candidate => candidate.url === '/api/product-labels/products');
    expect(request.request.params.get('search')).toBe('77001'); expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 }); await products;
  });

  it('encola el lote con su clave idempotente', async () => {
    const input = { profileId: 'profile', lines: [{ productId: 'product', quantity: 4 }] };
    const task = api.queue(input, 'same-key');
    const request = http.expectOne('/api/product-labels/jobs');
    expect(request.request.method).toBe('POST'); expect(request.request.headers.get('Idempotency-Key')).toBe('same-key'); expect(request.request.body).toEqual(input);
    request.flush({ jobId: 'job', labelCount: 4 }); expect((await task).labelCount).toBe(4);
  });

  it('consulta recepciones pendientes y prepara una compra concreta', async () => {
    const task = api.receipts(2, 10);
    const list = http.expectOne(request => request.url === '/api/product-labels/receipts');
    expect(list.request.params.get('page')).toBe('2'); expect(list.request.params.get('pageSize')).toBe('10');
    list.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 }); await task;
    const detail = api.receipt('purchase'); http.expectOne('/api/product-labels/receipts/purchase').flush({ purchaseId: 'purchase' });
    expect((await detail).purchaseId).toBe('purchase');
  });

  it('consulta los trabajos de etiquetas y reintenta un lote existente', async () => {
    const listTask = api.jobs(2, 10); const list = http.expectOne(request => request.url === '/api/product-labels/jobs');
    expect(list.request.params.get('page')).toBe('2'); list.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 }); await listTask;
    const retryTask = api.retry('job'); const retry = http.expectOne('/api/product-labels/jobs/job/retry');
    expect(retry.request.method).toBe('POST'); retry.flush({ jobId: 'job', status: 'PENDIENTE' }); expect((await retryTask).jobId).toBe('job');
  });
});
