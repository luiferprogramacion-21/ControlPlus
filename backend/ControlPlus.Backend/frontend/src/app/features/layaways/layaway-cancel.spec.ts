import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, AuthorizationPrompt } from '../../shared/authorization-dialog';
import { LayawayCancel } from './layaway-cancel';
import { LayawayDetail, LayawayPayment, LayawaysApi } from './layaways-api';

const payment = (appliedAmount: number, state = 'APLICADO'): LayawayPayment => ({ id: `p${appliedAmount}${state}`, number: 'PAP-1', methodCode: 'EFECTIVO', methodName: 'Efectivo',
  appliedAmount, receivedAmount: appliedAmount, change: 0, occurredAtUtc: '', state, correlationId: null });
const detail = (payments: LayawayPayment[] = [payment(20000), payment(30000)], version = 5): LayawayDetail => ({
  layaway: { id: 'a1', number: 'APT-1', customerId: 'c', customerName: 'Ana', createdAtUtc: '', dueOn: '', total: 100000, initialPayment: 20000, balance: 50000, state: 'ACTIVO', version, isOverdue: false },
  initialMinimumPercentage: 20, lines: [], payments, delivery: null, cancellation: null });

describe('LayawayCancel: cancelación y reembolso', () => {
  const api = { cancel: vi.fn() };
  const session = { user: () => ({ userName: 'admin' }) };
  let credentials = { userName: 'supervisor', password: 'secreto' };
  let prompt: AuthorizationPrompt | null; let dialogError: unknown;
  const dialog = { open: vi.fn(async (p: AuthorizationPrompt) => { prompt = p; dialogError = undefined; try { await p.run(credentials); return true; } catch (e) { dialogError = e; return false; } }) };
  let cancelled: unknown[];

  function open(d = detail()) {
    const fixture = TestBed.createComponent(LayawayCancel);
    fixture.componentRef.setInput('detail', d);
    cancelled = []; fixture.componentInstance.cancelled.subscribe(x => cancelled.push(x));
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    vi.clearAllMocks(); prompt = null; dialogError = undefined; credentials = { userName: 'supervisor', password: 'secreto' };
    api.cancel.mockResolvedValue({ number: 'APT-1', refunded: 0, retained: 50000 });
    TestBed.configureTestingModule({ providers: [{ provide: LayawaysApi, useValue: api }, { provide: AuthorizationDialog, useValue: dialog }, { provide: SessionService, useValue: session }] });
  });

  it('el máximo a devolver es lo realmente aplicado: ignora pagos revertidos', () => {
    const component = open(detail([payment(20000), payment(30000), payment(9999, 'REVERTIDO')]));
    expect(component.totalPaid()).toBe(50000);
  });

  it('calcula lo retenido', () => {
    const component = open(); component.refundAmount = 15000;
    expect(component.retained).toBe(35000);
  });

  it.each([
    ['negativo', -1, [], 'no negativo'],
    ['decimal', 100.5, [], 'no negativo'],
    ['mayor que lo abonado', 50001, [], 'no puede superar'],
    ['sin medios', 1000, [], 'al menos un medio'],
    ['suma distinta', 1000, [{ methodCode: 'EFECTIVO', appliedAmount: 800, receivedAmount: null, reference: '' }], 'sumar exactamente'],
    ['electrónico sin referencia', 1000, [{ methodCode: 'NEQUI', appliedAmount: 1000, receivedAmount: null, reference: '' }], 'referencia'],
  ])('valida: %s', async (_name, amount, drafts, text) => {
    const component = open(); component.refundAmount = amount;
    component.drafts = drafts as never[];
    expect(component.validation()).toContain(text);
    await component.confirm();
    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('rechaza una observación de más de 1000 caracteres', () => {
    const component = open(); component.observation = 'x'.repeat(1001);
    expect(component.validation()).toContain('1000');
  });

  it('cancela sin devolución: sin medios, sin motivo y con la versión del apartado', async () => {
    const component = open(); component.refundAmount = 0; component.observation = '  Cliente desiste ';
    await component.confirm();
    expect(prompt!.requireDifferentUser).toBe(true);
    expect(prompt!.detail).toContain('APT-1');
    expect(api.cancel).toHaveBeenCalledWith(expect.objectContaining({ id: 'a1', version: 5 }),
      { refundAmount: 0, refunds: [], reasonId: null, observation: 'Cliente desiste', authorization: { userName: 'supervisor', password: 'secreto' } }, expect.any(String));
    expect(cancelled).toHaveLength(1);
  });

  it('cancela con devolución mixta: efectivo sin referencia y electrónico con ella', async () => {
    const component = open(); component.refundAmount = 30000;
    component.drafts = [
      { methodCode: 'EFECTIVO', appliedAmount: 10000, receivedAmount: null, reference: 'ignorada' },
      { methodCode: 'NEQUI', appliedAmount: 20000, receivedAmount: null, reference: ' n-77 ' },
    ];
    await component.confirm();
    expect(api.cancel.mock.lastCall![1]).toMatchObject({ refundAmount: 30000, refunds: [
      { methodCode: 'EFECTIVO', amount: 10000, reference: null },
      { methodCode: 'NEQUI', amount: 20000, reference: 'n-77' },
    ], observation: null });
  });

  it('reutiliza la clave ante un reintento idéntico y exige el mismo autorizador', async () => {
    const component = open(); component.refundAmount = 0;
    api.cancel.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.confirm();
    const key = api.cancel.mock.lastCall![2];
    api.cancel.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.confirm();
    expect(api.cancel.mock.lastCall![2]).toBe(key);
    credentials = { userName: 'otro', password: 'x' };
    await component.confirm();
    expect((dialogError as Error).message).toContain('mismo usuario autorizador');
    expect(api.cancel).toHaveBeenCalledTimes(2);
    credentials = { userName: 'supervisor', password: 'secreto' };
    component.observation = 'cambió';
    await component.confirm();
    expect(api.cancel.mock.lastCall![2]).not.toBe(key);
  });

  it('traduce el 403 y no reutiliza la clave', async () => {
    const component = open(); component.refundAmount = 0;
    api.cancel.mockRejectedValueOnce(new HttpErrorResponse({ status: 403 }));
    await component.confirm();
    expect((dialogError as Error).message).toContain('No se autorizó la cancelación');
    const key = api.cancel.mock.lastCall![2];
    await component.confirm();
    expect(api.cancel.mock.lastCall![2]).not.toBe(key);
  });
});
