import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Establishment, EstablishmentService } from './establishment';

describe('EstablishmentService', () => {
  const current: Establishment = {
    commercialName: 'Baratísimo Ocaña', identification: '901234567-8', phone: '3150000000',
    address: 'Calle 1', receiptHeader: 'Compra local', receiptFooter: 'Gracias', version: 4,
  };
  let service: EstablishmentService; let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(EstablishmentService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('carga una vez y expone el nombre para toda la interfaz', async () => {
    const first = service.load();
    http.expectOne('/api/establishment').flush(current);
    expect(await first).toEqual(current); expect(service.name()).toBe('Baratísimo Ocaña');
    await service.load(); http.expectNone('/api/establishment');
  });

  it('actualiza con la versión esperada y refresca el valor compartido', async () => {
    const input = { commercialName: 'Mi negocio', identification: current.identification, phone: current.phone,
      address: current.address, receiptHeader: current.receiptHeader, receiptFooter: current.receiptFooter, expectedVersion: 4 };
    const request = service.update(input);
    const call = http.expectOne('/api/establishment');
    expect(call.request.method).toBe('PUT'); expect(call.request.body.expectedVersion).toBe(4);
    call.flush({ ...current, commercialName: 'Mi negocio', version: 5 });
    expect((await request).version).toBe(5); expect(service.name()).toBe('Mi negocio');
  });
});
