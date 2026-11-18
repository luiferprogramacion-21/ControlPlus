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
});
