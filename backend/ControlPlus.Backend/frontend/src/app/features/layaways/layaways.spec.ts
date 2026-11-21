import { ConfirmationDialog } from '../../shared/confirmation-dialog';
const confirmAction = vi.fn();
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Layaways } from './layaways';
import { Layaway, LayawayDetail, LayawaysApi } from './layaways-api';
import { CustomersApi } from '../customers/customers-api';
import { SessionService } from '../../core/session.service';

const layaway = (extra: Partial<Layaway> = {}): Layaway => ({ id: 'a1', number: 'APT-1', customerId: 'c1', customerName: 'Ana', createdAtUtc: '2026-11-01T10:00:00Z',
  dueOn: '2026-11-16', total: 100000, initialPayment: 20000, balance: 80000, state: 'ACTIVO', version: 3, isOverdue: false, ...extra });
const detail = (extra: Partial<Layaway> = {}): LayawayDetail => ({ layaway: layaway(extra), initialMinimumPercentage: 20, lines: [], payments: [], delivery: null, cancellation: null });

describe('Layaways: abonos y entrega', () => {
  const api = { list: vi.fn(), get: vi.fn(), applyPayment: vi.fn(), deliver: vi.fn() };
  const customers = { list: vi.fn() };
  const session = { has: (code: string) => ['LAYAWAY.MANAGE', 'CUSTOMERS.BASIC_MANAGE'].includes(code) };
  let component: Layaways;

  beforeEach(() => {
    vi.clearAllMocks();
    api.list.mockResolvedValue({ items: [layaway()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    api.get.mockResolvedValue(detail());
    api.applyPayment.mockResolvedValue({ layawayId: 'a1', balance: 30000, state: 'ACTIVO', version: 4, correlationId: 'c', change: 0, payments: [] });
    api.deliver.mockResolvedValue({ layawayId: 'a1', number: 'APT-1', state: 'ENTREGADO', version: 5 });
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmationDialog, useValue: { confirm: confirmAction } },
      { provide: LayawaysApi, useValue: api }, { provide: CustomersApi, useValue: customers }, { provide: SessionService, useValue: session },
    ] });
    confirmAction.mockResolvedValue(true);
    component = TestBed.runInInjectionContext(() => new Layaways());
  });

  it('lista con los filtros elegidos, incluido el vencimiento como booleano', async () => {
    component.state = 'ACTIVO'; component.overdue = 'true'; component.dueFrom = '2026-11-01'; component.customerId = 'c1';
    component.find(); await vi.waitFor(() => expect(api.list).toHaveBeenCalled());
    expect(api.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, state: 'ACTIVO', customerId: 'c1', dueFrom: '2026-11-01', dueTo: undefined, overdue: true });
    component.overdue = ''; component.find();
    await vi.waitFor(() => expect(api.list).toHaveBeenCalledTimes(2));
    expect(api.list.mock.lastCall![0].overdue).toBeUndefined();
  });

  it('rechaza un intervalo de fecha límite invertido', () => {
    component.dueFrom = '2026-12-01'; component.dueTo = '2026-11-01';
    component.find();
    expect(api.list).not.toHaveBeenCalled();
    expect(component.error()).toContain('intervalo');
  });

  it('solo abona un apartado ACTIVO con saldo y solo entrega uno PAGADO con saldo cero', () => {
    expect(component.canPay(layaway())).toBe(true);
    expect(component.canPay(layaway({ state: 'PAGADO', balance: 0 }))).toBe(false);
    expect(component.canPay(layaway({ state: 'ENTREGADO', balance: 0 }))).toBe(false);
    expect(component.canPay(layaway({ state: 'ANULADO' }))).toBe(false);
    expect(component.canDeliver(layaway({ state: 'PAGADO', balance: 0 }))).toBe(true);
    expect(component.canDeliver(layaway())).toBe(false);
    expect(component.canDeliver(layaway({ state: 'PAGADO', balance: 10 }))).toBe(false);
    expect(component.canDeliver(layaway({ state: 'ENTREGADO', balance: 0 }))).toBe(false);
  });

  it('un apartado vencido sigue admitiendo abonos: vencido es solo un indicador', () => {
    expect(component.canPay(layaway({ isOverdue: true }))).toBe(true);
    expect(component.label('ACTIVO')).toBe('Activo');
  });

  it('abre el abono con el detalle recién leído y no con la fila de la lista', async () => {
    api.get.mockResolvedValue(detail({ version: 9, balance: 50000 }));
    await component.openPayment(layaway({ version: 3 }));
    expect(api.get).toHaveBeenCalledWith('a1');
    expect(component.paymentDialog()).toBe(true);
    expect(component.detail()!.layaway.version).toBe(9);
  });

  it('no abre el abono si el detalle actual ya no admite pagos', async () => {
    api.get.mockResolvedValue(detail({ state: 'PAGADO', balance: 0 }));
    await component.openPayment(layaway());
    expect(component.paymentDialog()).toBe(false);
  });

  it.each([
    ['medio repetido', [{ m: 'EFECTIVO', a: 1000, r: 1000, ref: '' }, { m: 'EFECTIVO', a: 1000, r: 1000, ref: '' }], 'una sola vez'],
    ['importe cero', [{ m: 'EFECTIVO', a: 0, r: 0, ref: '' }], 'entero mayor que cero'],
    ['importe decimal', [{ m: 'EFECTIVO', a: 10.5, r: 11, ref: '' }], 'entero mayor que cero'],
    ['supera el saldo', [{ m: 'EFECTIVO', a: 80001, r: 90000, ref: '' }], 'supera el saldo'],
    ['efectivo recibido menor', [{ m: 'EFECTIVO', a: 5000, r: 4000, ref: '' }], 'efectivo recibido'],
    ['electrónico con referencia larga', [{ m: 'NEQUI', a: 5000, r: null, ref: 'x'.repeat(201) }], 'referencia'],
  ])('valida: %s', async (_name, rows, text) => {
    await component.openPayment(layaway());
    component.drafts = rows.map(x => ({ methodCode: x.m as 'EFECTIVO', appliedAmount: x.a, receivedAmount: x.r as number | null, reference: x.ref }));
    expect(component.validation()).toContain(text);
    await component.savePayment();
    expect(api.applyPayment).not.toHaveBeenCalled();
  });

  it('envía un abono mixto sin referencia en efectivo y con ella en el electrónico', async () => {
    await component.openPayment(layaway());
    component.drafts = [
      { methodCode: 'EFECTIVO', appliedAmount: 10000, receivedAmount: 20000, reference: 'ignorada' },
      { methodCode: 'NEQUI', appliedAmount: 5000, receivedAmount: null, reference: ' ref-77 ' },
    ];
    expect(component.appliedTotal).toBe(15000);
    expect(component.change).toBe(10000);
    await component.savePayment();
    expect(api.applyPayment).toHaveBeenCalledWith(expect.objectContaining({ id: 'a1', version: 3 }), [
      { methodCode: 'EFECTIVO', appliedAmount: 10000, receivedAmount: 20000, reference: null },
      { methodCode: 'NEQUI', appliedAmount: 5000, receivedAmount: null, reference: 'ref-77' },
    ], expect.any(String));
    expect(component.paymentDialog()).toBe(false);
    expect(component.notice()).toBe('Abono registrado.');
  });

  it('reutiliza la clave ante un reintento idéntico y la cambia si cambia el contenido', async () => {
    await component.openPayment(layaway());
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 5000, receivedAmount: 5000, reference: '' }];
    api.applyPayment.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.savePayment();
    const firstKey = api.applyPayment.mock.lastCall![2];
    expect(component.error()).not.toBe('');
    await component.savePayment();
    expect(api.applyPayment.mock.lastCall![2]).toBe(firstKey);
    await component.openPayment(layaway());
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 6000, receivedAmount: 6000, reference: '' }];
    await component.savePayment();
    expect(api.applyPayment.mock.lastCall![2]).not.toBe(firstKey);
  });

  it('avisa cuando el último abono deja el apartado pagado', async () => {
    api.applyPayment.mockResolvedValue({ layawayId: 'a1', balance: 0, state: 'PAGADO', version: 4, correlationId: 'c', change: 0, payments: [] });
    await component.openPayment(layaway());
    component.drafts = [{ methodCode: 'EFECTIVO', appliedAmount: 80000, receivedAmount: 80000, reference: '' }];
    await component.savePayment();
    expect(component.notice()).toContain('pagado');
  });

  it('entrega solo tras confirmar y con la versión del detalle', async () => {
    api.get.mockResolvedValue(detail({ state: 'PAGADO', balance: 0, version: 6 }));
    await component.open(layaway());
    confirmAction.mockResolvedValueOnce(false);
    await component.deliver();
    expect(api.deliver).not.toHaveBeenCalled();
    await component.deliver();
    expect(api.deliver).toHaveBeenCalledWith(expect.objectContaining({ id: 'a1', version: 6 }), expect.any(String));
    expect(component.notice()).toBe('Productos entregados.');
  });

  it('no entrega un apartado activo', async () => {
    await component.open(layaway());
    await component.deliver();
    expect(confirmAction).not.toHaveBeenCalled();
    expect(api.deliver).not.toHaveBeenCalled();
  });

  it('conserva la clave de entrega al reintentar tras un fallo de red', async () => {
    api.get.mockResolvedValue(detail({ state: 'PAGADO', balance: 0 }));
    await component.open(layaway());
    api.deliver.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    await component.deliver();
    const key = api.deliver.mock.lastCall![1];
    expect(component.error()).not.toBe('');
    await component.deliver();
    expect(api.deliver.mock.lastCall![1]).toBe(key);
  });

  it('muestra el error de la API al fallar la entrega y no cierra el detalle', async () => {
    api.get.mockResolvedValue(detail({ state: 'PAGADO', balance: 0 }));
    await component.open(layaway());
    api.deliver.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'Stock insuficiente.' } }));
    await component.deliver();
    expect(component.error()).toBe('Stock insuficiente.');
    expect(component.detail()).not.toBeNull();
  });
});
