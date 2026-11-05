import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SuppliersApi, Supplier } from './suppliers-api';
describe('SuppliersApi', () => {
  let api: SuppliersApi; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); api = TestBed.inject(SuppliersApi); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('envía los filtros reales y paginación', async () => { const task = api.list({ name: 'Sol', documentType: 'NIT', documentNumber: '123', isActive: false, page: 2, pageSize: 10 }); const req = http.expectOne(r => r.url === '/api/suppliers'); expect(req.request.params.get('name')).toBe('Sol'); expect(req.request.params.get('documentNumber')).toBe('123'); expect(req.request.params.get('isActive')).toBe('false'); expect(req.request.params.get('page')).toBe('2'); req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 }); await task; });
  it('envía versión al editar y cambiar estado', async () => { const s = { id: 'id', version: 7, isActive: true } as Supplier; const input = { documentType: null, documentNumber: null, name: 'Sol', phone: null, address: null, email: null, observations: null }; const update = api.update(s, input); const u = http.expectOne('/api/suppliers/id'); expect(u.request.body.expectedVersion).toBe(7); u.flush(s); await update; const status = api.status(s); const st = http.expectOne('/api/suppliers/id/status'); expect(st.request.body).toEqual({ isActive: false, expectedVersion: 7 }); st.flush(s); await status; });
});
