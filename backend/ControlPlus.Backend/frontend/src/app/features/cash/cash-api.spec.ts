import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CashApi } from './cash-api';

describe('CashApi: cierre con diferencia', () => {
  let api: CashApi;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CashApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lee el catálogo y envía motivo, versión y autorización al cierre', async () => {
    const reasons = api.closureDifferenceReasons();
    const read = http.expectOne('/api/cash/closure-difference-reasons');
    expect(read.request.method).toBe('GET');
    read.flush([{ id: 'reason-1', code: 'FALTANTE', name: 'Faltante', isActive: true }]);
    expect((await reasons)[0].name).toBe('Faltante');

    const input = { version: 4, paymentMethods: [{ paymentMethodId: 'cash-1', countedAmount: 90 }],
      differenceReasonId: 'reason-1', differenceObservation: 'Verificado',
      authorization: { userName: 'supervisor', password: 'clave' } };
    const close = api.close('shift-1', input);
    const request = http.expectOne('/api/cash/shifts/shift-1/close');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    request.flush({ shiftId: 'shift-1', shiftVersion: 5, totalExpected: 100, totalCounted: 90,
      totalDifference: -10, cashDifference: -10, paymentMethods: [] });
    expect((await close).totalDifference).toBe(-10);
  });
});
