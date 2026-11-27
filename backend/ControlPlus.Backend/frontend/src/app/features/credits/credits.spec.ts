import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session.service';
import { Credit, CreditPayment, CreditsApi } from './credits-api';
import { CustomersApi } from '../customers/customers-api';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { Credits } from './credits';
import { AuthorizationDialog } from '../../shared/authorization-dialog';
import { OperatorCard } from '../../shared/operator-card';

const credit = { id: 'credit-id', saleNumber: 'VTA-1', customerName: 'Ana', adjustedAmount: 10000, balance: 5000, state: 'PENDIENTE', version: 4 } as Credit;
const payment = { id: 'payment-id', number: 'AB-1', appliedAmount: 2000, state: 'APLICADO' } as CreditPayment;

async function settle(): Promise<void> {
  for (let tick = 0; tick < 10; tick++) await Promise.resolve();
}

describe('Credits', () => {
  let component: Credits; let fixture: ComponentFixture<Credits>; let http: HttpTestingController;
  let onRequest: ((url: string) => void) | null;
  beforeEach(async () => {
    onRequest = null;
    await TestBed.configureTestingModule({ imports: [Credits], providers: [provideHttpClient(withInterceptors([(request, next) => { onRequest?.(request.url); return next(request); }])), provideHttpClientTesting(), { provide: SessionService, useValue: { has: (permission: string) => permission === 'CREDIT.MANAGE', hasRole: (...roles: string[]) => roles.includes('ADMINISTRADOR'), user: () => ({ userName: 'admin' }) } }, { provide: AuthorizationDialog, useValue: { open: async (prompt: { run: (credentials: null) => Promise<void> }) => prompt.run(null) } }, { provide: OperatorCard, useValue: { ensure: async () => ({ shared: false, role: null, sessionId: null }) } }] }).compileComponents();
    fixture = TestBed.createComponent(Credits); component = fixture.componentInstance; http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('impide abonos superiores al saldo y medios repetidos antes de enviar', () => {
    component.selected.set(credit);
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 6000, receivedAmount: 6000, reference: '' }];
    expect(component.validation()).toContain('supera el saldo');
    component.drafts = [{ methodCode: 'NEQUI', appliedAmount: 2000, receivedAmount: null, reference: 'A' }, { methodCode: 'NEQUI', appliedAmount: 2000, receivedAmount: null, reference: 'B' }];
    expect(component.validation()).toContain('una sola vez');
  });
  it('muestra el detalle en una ventana modal y no debajo de la lista', () => {
    component.selected.set(credit); component.detailOpen.set(true); fixture.detectChanges();
    http.expectOne(r => r.url === '/api/credits').flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    fixture.detectChanges();
    const modal = fixture.nativeElement.querySelector('[role="dialog"][aria-labelledby="credit-detail-title"]');
    expect(modal).toBeTruthy(); expect(modal.textContent).toContain('VTA-1');
    expect(fixture.nativeElement.querySelector('section.credit-detail')).toBeNull();
    modal.querySelector('.module-form-sheet__backdrop').click(); fixture.detectChanges();
    expect(component.detailOpen()).toBe(false);
  });
  it('bloquea un segundo envío mientras espera respuesta y usa saldo del servidor', async () => {
    let detailRequested!: () => void;
    let listRequested!: () => void;
    let postCount = 0;
    const detailStarted = new Promise<void>(resolve => { detailRequested = resolve; });
    const listStarted = new Promise<void>(resolve => { listRequested = resolve; });
    onRequest = url => {
      if (url === '/api/credits/credit-id/payments') postCount++;
      if (url === '/api/credits/credit-id') detailRequested();
      if (url === '/api/credits') listRequested();
    };
    component.selected.set(credit);
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 5000, receivedAmount: 6000, reference: '' }];
    const first = component.save(); const second = component.save();
    // Antes de enviar se revisa quién opera (turno compartido): el abono sale después de esa consulta.
    for (let tick = 0; tick < 10; tick++) await Promise.resolve();
    const request = http.expectOne('/api/credits/credit-id/payments');
    expect(request.request.body.expectedVersion).toBe(4);
    expect(postCount).toBe(1);
    request.flush({ creditId: 'credit-id', balance: 0, state: 'PAGADO', version: 5, correlationId: 'correlation', payments: [] });
    await detailStarted;
    const detail = http.expectOne('/api/credits/credit-id'); detail.flush({ ...credit, balance: 0, state: 'PAGADO', version: 5 });
    await listStarted;
    const list = http.expectOne(r => r.url === '/api/credits');
    expect(list.request.params.get('page')).toBe('1');
    expect(list.request.params.get('pageSize')).toBe('20');
    list.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    await Promise.all([first, second]);
    expect(postCount).toBe(1);
    expect(component.selected()?.balance).toBe(0);
    expect(component.result()?.items).toEqual([]);
  });

  it('reutiliza la clave del abono al reintentar un fallo de red y la cambia si cambia el importe', async () => {
    component.selected.set(credit);
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: '' }];

    const first = component.save();
    await settle();
    const firstRequest = http.expectOne('/api/credits/credit-id/payments');
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    expect(firstKey).toBeTruthy();
    expect(firstRequest.request.body).toEqual({ expectedVersion: 4, payments: [{ methodCode: 'EFECTIVO', appliedAmount: 2000, receivedAmount: 2000, reference: null }] });
    firstRequest.flush({ message: 'Sin conexión' }, { status: 503, statusText: 'Unavailable' });
    await first;
    expect(component.saving()).toBe(false);

    const retry = component.save();
    await settle();
    const retryRequest = http.expectOne('/api/credits/credit-id/payments');
    expect(retryRequest.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryRequest.flush({ message: 'Sin conexión' }, { status: 503, statusText: 'Unavailable' });
    await retry;

    component.drafts[0].appliedAmount = 3000;
    component.drafts[0].receivedAmount = 3000;
    const changed = component.save();
    await settle();
    const changedRequest = http.expectOne('/api/credits/credit-id/payments');
    expect(changedRequest.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    expect(changedRequest.request.body.payments[0].appliedAmount).toBe(3000);
    changedRequest.flush({ message: 'Sin conexión' }, { status: 503, statusText: 'Unavailable' });
    await changed;
  });

  it('exige observación para Otro y conserva la clave al reintentar anular un abono', async () => {
    component.selected.set(credit);
    const opening = component.startCancelPayment(payment);
    http.expectOne('/api/credits/payment-cancellation-reasons').flush([{ id: 'other-id', code: 'OTRO', name: 'Otro', displayOrder: 1 }]);
    await opening;
    expect(component.cancellingPayment()).toBe(true);
    component.cancellationReasonId = 'other-id';
    await component.confirmCancelPayment();
    expect(component.error()).toContain('observación');
    http.expectNone('/api/credits/credit-id/payments/payment-id/cancel');

    component.cancellationObservation = '  Pago duplicado  ';
    const first = component.confirmCancelPayment();
    await settle();
    const firstRequest = http.expectOne('/api/credits/credit-id/payments/payment-id/cancel');
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    expect(firstKey).toBeTruthy();
    expect(firstRequest.request.body).toEqual({ expectedVersion: 4, reasonId: 'other-id', observation: 'Pago duplicado' });
    const rejection = expect(first).rejects.toBeDefined();
    firstRequest.flush({ message: 'Sin conexión' }, { status: 503, statusText: 'Unavailable' });
    await rejection;
    expect(component.saving()).toBe(false);

    const retry = component.confirmCancelPayment();
    await settle();
    const retryRequest = http.expectOne('/api/credits/credit-id/payments/payment-id/cancel');
    expect(retryRequest.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryRequest.flush({ reversedAmount: 2000 });
    await settle();
    http.expectOne('/api/credits/credit-id').flush({ ...credit, balance: 7000, version: 5 });
    await settle();
    http.expectOne(request => request.url === '/api/credits').flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    await retry;
    expect(component.cancellingPayment()).toBe(false);
    expect(component.selected()?.balance).toBe(7000);
  });

  it('valida la condonación, conserva la clave al reintentar y actualiza el detalle', async () => {
    component.selected.set(credit);
    component.startForgive();
    expect(component.forgiving()).toBe(true);
    await component.confirmForgive();
    expect(component.error()).toContain('motivo');
    http.expectNone('/api/credits/credit-id/forgive');

    component.forgivenessReason = '  Acuerdo aprobado  ';
    const first = component.confirmForgive();
    await settle();
    const firstRequest = http.expectOne('/api/credits/credit-id/forgive');
    const firstKey = firstRequest.request.headers.get('Idempotency-Key');
    expect(firstKey).toBeTruthy();
    expect(firstRequest.request.body).toEqual({ expectedVersion: 4, reason: 'Acuerdo aprobado' });
    firstRequest.flush({ message: 'Sin conexión' }, { status: 503, statusText: 'Unavailable' });
    await first;
    expect(component.forgiving()).toBe(true);

    const retry = component.confirmForgive();
    await settle();
    const retryRequest = http.expectOne('/api/credits/credit-id/forgive');
    expect(retryRequest.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryRequest.flush({ creditId: 'credit-id', balance: 0, state: 'ANULADO', version: 5 });
    await settle();
    http.expectOne('/api/credits/credit-id').flush({ ...credit, balance: 0, state: 'ANULADO', version: 5 });
    await settle();
    http.expectOne(request => request.url === '/api/credits').flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    await retry;
    expect(component.forgiving()).toBe(false);
    expect(component.selected()?.state).toBe('ANULADO');
  });
});

describe('Credits: llega desde Clientes con «Cobrar abono» (pruebas del 26/11/2026)', () => {
  it('filtra por el cliente y deja listo el abono del crédito elegido', async () => {
    const pending = { ...credit, id: 'k1', balance: 4000, state: 'PENDIENTE' };
    const api = { list: vi.fn().mockResolvedValue({ items: [pending], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 }) };
    TestBed.configureTestingModule({ providers: [
      { provide: CreditsApi, useValue: api }, { provide: CustomersApi, useValue: {} },
      { provide: SessionService, useValue: { has: () => true, hasRole: () => true, user: () => ({ userName: 'admin' }) } },
      { provide: AuthorizationDialog, useValue: {} }, { provide: OperatorCard, useValue: {} },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ cliente: 'c1', nombre: 'Ana', abonar: 'k1' }) } } },
    ] });
    const component = TestBed.runInInjectionContext(() => new Credits());
    component.ngOnInit();
    await vi.waitFor(() => expect(component.dialog()).toBe(true));
    expect(api.list).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c1' }));
    expect(component.customerLabel()).toBe('Ana');
    expect(component.selected()?.id).toBe('k1');
    expect(component.drafts[0].appliedAmount).toBe(4000);
  });
});
