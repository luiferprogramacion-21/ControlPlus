import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { CustomersApi } from './customers-api';

describe('CustomersApi', () => {
  let api: CustomersApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(CustomersApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('envía filtros de búsqueda y paginación', async () => {
    const task = api.list({ search: 'Ana', status: 'active', page: 2, pageSize: 10 });
    const req = http.expectOne(r => r.url === '/api/customers' && r.params.get('search') === 'Ana' && r.params.get('page') === '2' && r.params.get('pageSize') === '10');
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 });
    expect((await task).items).toEqual([]);
  });
  it('envía la versión al editar y cambiar estado', async () => {
    const customer = { id: 'id', version: 4, isActive: true } as any;
    const update = api.update(customer, { documentType: null, documentNumber: null, fullName: 'Ana', phone: null, address: 'Calle 1', email: null, observations: null });
    const request = http.expectOne('/api/customers/id'); expect(request.request.body.expectedVersion).toBe(4); request.flush(customer); await update;
    const status = api.status(customer); const statusRequest = http.expectOne('/api/customers/id/status'); expect(statusRequest.request.body).toEqual({ isActive: false, expectedVersion: 4 }); statusRequest.flush(customer); await status;
  });
});
