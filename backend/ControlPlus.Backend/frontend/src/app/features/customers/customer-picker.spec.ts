import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { CustomerPicker } from './customer-picker';
import { Customer, CustomersApi } from './customers-api';

const customer = (over: Partial<Customer> = {}): Customer => ({ id: 'c1', documentType: 'CC', documentNumber: '123', fullName: 'Ana Pérez', phone: '3001234567', address: 'Calle 1', email: null, observations: null, isActive: true, version: 1, ...over });

describe('CustomerPicker', () => {
  const api = { list: vi.fn(), create: vi.fn(), creditStages: vi.fn() };
  let permissions: string[];
  const session = { has: (p: string) => permissions.includes(p) };
  let picker: CustomerPicker; let chosen: (Customer | null)[];

  async function open(required = false) {
    const fixture = TestBed.createComponent(CustomerPicker);
    fixture.componentRef.setInput('required', required);
    chosen = []; fixture.componentInstance.chosen.subscribe(x => chosen.push(x));
    fixture.detectChanges(); await fixture.whenStable();
    picker = fixture.componentInstance;
  }

  beforeEach(() => {
    vi.useFakeTimers(); vi.clearAllMocks(); permissions = ['CUSTOMERS.BASIC_MANAGE'];
    api.list.mockResolvedValue({ items: [customer()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    api.create.mockImplementation(async (input: Record<string, unknown>) => ({ ...customer({ id: 'new' }), ...input }));
    TestBed.configureTestingModule({ providers: [{ provide: CustomersApi, useValue: api }, { provide: SessionService, useValue: session }] });
  });
  afterEach(() => vi.useRealTimers());

  it('muestra el color del crédito en la lista al elegir el cliente, solo a quien vende a crédito (punto 2)', async () => {
    permissions = ['CUSTOMERS.BASIC_MANAGE', 'CREDIT.REQUEST'];
    api.list.mockResolvedValue({ items: [customer(), customer({ id: 'c2', fullName: 'Luis' }), customer({ id: 'c3', fullName: 'Sin deuda' })], page: 1, pageSize: 20, totalCount: 3, totalPages: 1 });
    api.creditStages.mockResolvedValue([
      { customerId: 'c1', stage: 'VENCIDO', outstandingBalance: 50000, overdueBalance: 20000, oldestOverdueOn: '2026-11-20' },
      { customerId: 'c2', stage: 'POR_VENCER', outstandingBalance: 10000, overdueBalance: 0, nextDueOn: '2026-11-26' },
      { customerId: 'c3', stage: 'SIN_DEUDA', outstandingBalance: 0, overdueBalance: 0 },
    ]);
    const fixture = TestBed.createComponent(CustomerPicker);
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(api.creditStages).toHaveBeenCalledWith(['c1', 'c2', 'c3']);
    await vi.waitFor(() => expect(fixture.componentInstance.stages()['c1']).toBeDefined());
    fixture.detectChanges();
    const pills = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.customer-result .status-pill')).map(x => x.textContent?.trim());
    expect(pills).toEqual(['Moroso', 'Por vencer']);
  });

  it('sin permiso de crédito no consulta la etapa', async () => {
    await open();
    expect(api.creditStages).not.toHaveBeenCalled();
    expect(picker.stageOf(customer())).toBeNull();
  });

  it('al abrir lista los clientes activos sin esperar a que se escriba', async () => {
    await open();
    expect(api.list).toHaveBeenCalledWith({ search: '', status: 'active', page: 1, pageSize: 20 });
    expect(picker.results()).toHaveLength(1);
  });

  it('busca mientras se escribe, con una pausa, solo por el último texto', async () => {
    await open(); api.list.mockClear();
    picker.onSearch('an'); await vi.advanceTimersByTimeAsync(100); picker.onSearch('ana'); await vi.advanceTimersByTimeAsync(300);
    expect(api.list).toHaveBeenCalledTimes(1); expect(api.list.mock.calls[0][0].search).toBe('ana');
  });

  it('elige un cliente y permite consumidor final solo si no es obligatorio', async () => {
    await open(); picker.choose(picker.results()[0]); picker.consumerFinal();
    expect(chosen).toEqual([customer(), null]);
  });

  it('para un crédito bloquea al cliente sin documento completo ni teléfono', async () => {
    await open(true);
    const sinDatos = customer({ id: 'x', documentType: null, documentNumber: null, phone: null });
    expect(picker.eligible(sinDatos)).toBe(false); picker.choose(sinDatos); expect(chosen).toEqual([]);
    expect(picker.eligible(customer({ documentType: null, documentNumber: null }))).toBe(true);
  });

  it('exige nombre, documento, teléfono y dirección para guardar, en contado y en crédito (21 de noviembre de 2026)', async () => {
    for (const credit of [false, true]) {
      await open(credit); picker.startCreate();
      expect(picker.validation()).toContain('nombre');
      picker.form.fullName = 'Luis'; expect(picker.validation()).toContain('documento');
      picker.form.documentType = 'CC'; expect(picker.validation()).toContain('documento');
      picker.form.documentNumber = ' 1234567 '; expect(picker.validation()).toContain('teléfono');
      picker.form.phone = '3001112233'; expect(picker.validation()).toContain('dirección');
      picker.form.address = 'Calle 2'; expect(picker.validation()).toBe('');
    }
  });

  it('envía los cuatro datos normalizados y selecciona al creado', async () => {
    await open(true); picker.startCreate();
    Object.assign(picker.form, { fullName: ' Luis ', documentType: 'CC', documentNumber: ' 1234567 ', phone: ' 3001234567 ', address: ' Calle 2 ' });
    await picker.create();
    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ documentType: 'CC', documentNumber: '1234567', fullName: 'Luis', phone: '3001234567', address: 'Calle 2' }));
    expect(chosen[0]?.id).toBe('new');
  });
  it('rellena el formulario con lo escrito: texto como nombre, números como teléfono', async () => {
    await open(); picker.search = 'Laura Martínez'; picker.startCreate(); expect(picker.form).toMatchObject({ fullName: 'Laura Martínez', phone: '' });
    picker.search = '3001234567'; picker.startCreate(); expect(picker.form).toMatchObject({ fullName: '', phone: '3001234567' });
  });

  it('sin permiso de clientes no ofrece registrar', async () => {
    permissions = []; await open(); expect(picker.canCreate).toBe(false);
  });
});
