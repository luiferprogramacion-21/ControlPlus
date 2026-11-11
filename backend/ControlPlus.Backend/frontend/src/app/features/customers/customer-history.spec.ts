import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Customer, CustomersApi } from './customers-api';
import { CustomerHistory } from './customer-history';

const customer = { id: 'c1', fullName: 'Ana Pérez', documentType: 'CC', documentNumber: '123', phone: '300', address: 'Calle 1', email: null, observations: null, isActive: true, version: 1 } as Customer;
const item = (type: string, over = {}) => ({ id: `${type}-1`, type, parentId: null, number: 'VTA-1', occurredAt: '2026-11-06T15:00:00Z', status: 'CONFIRMADA', amount: 1000, outstandingAmount: null, ...over });

describe('CustomerHistory', () => {
  const api = { history: vi.fn() };
  async function open() {
    const fixture = TestBed.createComponent(CustomerHistory);
    fixture.componentRef.setInput('customer', customer);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }
  beforeEach(() => {
    vi.clearAllMocks();
    api.history.mockResolvedValue({ items: [item('SALE'), item('CREDIT', { outstandingAmount: 500 }), item('CREDIT_PAYMENT', { parentId: 'CREDIT-1' })], page: 1, pageSize: 20, totalCount: 3, totalPages: 1 });
    TestBed.configureTestingModule({ providers: [{ provide: CustomersApi, useValue: api }] });
  });

  it('carga la primera página del cliente al abrir', async () => {
    const fixture = await open();
    expect(api.history).toHaveBeenCalledWith('c1', 1, 20);
    expect(fixture.componentInstance.result()?.items).toHaveLength(3);
  });
  it('traduce tipos y estados y distingue los abonos', async () => {
    const c = (await open()).componentInstance;
    expect(c.typeLabel('SALE')).toBe('Venta'); expect(c.typeLabel('LAYAWAY_PAYMENT')).toBe('Abono de apartado'); expect(c.typeLabel('NUEVO')).toBe('NUEVO');
    expect(c.statusLabel('ANULADA')).toBe('Anulada'); expect(c.statusLabel('OTRO')).toBe('OTRO');
    expect(c.isPayment('CREDIT_PAYMENT')).toBe(true); expect(c.isPayment('SALE')).toBe(false);
  });
  it('muestra el saldo solo en créditos y apartados', async () => {
    const text = ((await open()).nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Historial de Ana Pérez'); expect(text).toContain('Abono de crédito');
  });
  it('un permiso o error del servidor se muestra y se puede reintentar', async () => {
    api.history.mockRejectedValueOnce({ status: 403 });
    const fixture = await open(); expect(fixture.componentInstance.error()).not.toBe('');
    await fixture.componentInstance.load(); expect(fixture.componentInstance.error()).toBe('');
  });
  it('cambiar de página pide la siguiente', async () => {
    const c = (await open()).componentInstance; api.history.mockClear();
    c.pageChanged({ pageIndex: 1, pageSize: 10, length: 30, previousPageIndex: 0 }); await Promise.resolve();
    expect(api.history).toHaveBeenCalledWith('c1', 2, 10);
  });
});
