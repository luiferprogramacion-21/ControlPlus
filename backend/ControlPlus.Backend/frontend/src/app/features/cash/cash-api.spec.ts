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

  it('envía la autorización del movimiento de caja solo cuando la hay (punto 13)', async () => {
    const authorized = api.movement('expenses', 5000, 'Bolsas', null, { credentialToken: '12345678901234' });
    const first = http.expectOne('/api/cash/movements/expenses');
    expect(first.request.body).toEqual({ amount: 5000, concept: 'Bolsas', operatorSessionId: null, authorization: { credentialToken: '12345678901234' } });
    first.flush({ id: 'm1', type: 'GASTO', category: 'GASTO', amount: 5000, concept: 'Bolsas', occurredAtUtc: '' });
    await authorized;
    const alone = api.movement('incomes', 1000, 'Base', 'session-1');
    const second = http.expectOne('/api/cash/movements/incomes');
    expect(second.request.body).toEqual({ amount: 1000, concept: 'Base', operatorSessionId: 'session-1' });
    second.flush({ id: 'm2', type: 'INGRESO', category: 'INGRESO', amount: 1000, concept: 'Base', occurredAtUtc: '' });
    await alone;
  });
});
