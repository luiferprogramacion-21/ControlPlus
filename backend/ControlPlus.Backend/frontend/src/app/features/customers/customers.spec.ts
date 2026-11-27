import { TestBed } from '@angular/core/testing';
import { Customers } from './customers';
import { CustomersApi } from './customers-api';
import { SessionService } from '../../core/session.service';
import { Router, provideRouter } from '@angular/router';
import { CreditsApi } from '../credits/credits-api';

describe('Customers', () => {
  let component: Customers;
  const api = { list: vi.fn(), create: vi.fn(), update: vi.fn(), status: vi.fn(), get: vi.fn(), creditStages: vi.fn(), creditSummary: vi.fn() };
  const credits = { list: vi.fn() };
  let permissions = ['CUSTOMERS.MANAGE'];
  const session = { has: (permission: string) => permissions.includes(permission) };
  beforeEach(() => { vi.clearAllMocks(); permissions = ['CUSTOMERS.MANAGE']; TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: CustomersApi, useValue: api }, { provide: CreditsApi, useValue: credits }, { provide: SessionService, useValue: session }] }); component = TestBed.runInInjectionContext(() => new Customers()); });
  it('valida la pareja documental antes del alta; la dirección es opcional (21 de noviembre de 2026)', async () => {
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.documentType = 'CC';
    await component.save(); expect(api.create).not.toHaveBeenCalled();
    component.form.address = 'x'.repeat(301); component.form.documentType = null;
    await component.save(); expect(api.create).not.toHaveBeenCalled();
  });
  it('muestra si cada cliente está al día, por vencer o moroso (auditoría C.14)', async () => {
    permissions = ['CUSTOMERS.MANAGE', 'CREDIT.REQUEST'];
    api.list.mockResolvedValue({ items: [{ id: 'c1' }, { id: 'c2' }], page: 1, pageSize: 20, totalCount: 2, totalPages: 1 });
    api.creditStages.mockResolvedValue([{ customerId: 'c1', stage: 'VENCIDO', outstandingBalance: 9000, overdueBalance: 9000, oldestOverdueOn: '2026-11-01' }]);
    await component.reload(); await Promise.resolve();
    expect(api.creditStages).toHaveBeenCalledWith(['c1', 'c2']);
    expect(component.creditStageView(component.stages()['c1'])?.label).toBe('Moroso');
    expect(component.stages()['c2']).toBeUndefined();
    // Pruebas del usuario del 26/11/2026: la lista muestra lo que debe cada cliente en total.
    expect(component.debt('c1')).toBe('$ 9.000');
    expect(component.debt('c2')).toBe('—');
    permissions = ['CUSTOMERS.MANAGE']; api.creditStages.mockClear();
    await component.reload();
    expect(api.creditStages).not.toHaveBeenCalled();
  });
  it('resume lo que deben, filtra por grupo y lleva a cobrar el abono (pruebas del 26/11/2026)', async () => {
    permissions = ['CUSTOMERS.MANAGE', 'CREDIT.REQUEST', 'CREDIT.MANAGE'];
    const customer = { id: 'c1', fullName: 'Carlos Ruiz', documentType: 'CC', documentNumber: '79', phone: '3104567890', address: 'Calle 5', isActive: true, version: 1 };
    api.list.mockResolvedValue({ items: [customer], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    api.creditStages.mockResolvedValue([{ customerId: 'c1', stage: 'VENCIDO', outstandingBalance: 90000, overdueBalance: 90000, oldestOverdueOn: '2026-11-10' }]);
    api.creditSummary.mockResolvedValue({ debtors: 6, debtTotal: 1250000, overdue: 1, overdueTotal: 90000, dueSoon: 1, dueSoonTotal: 60000 });
    component.setFilter('MOROSOS'); await vi.waitFor(() => expect(component.summary()).not.toBeNull());
    expect(api.list).toHaveBeenLastCalledWith(expect.objectContaining({ credit: 'MOROSOS', page: 1 }));
    await vi.waitFor(() => expect(component.stageText('c1')).toBe('Moroso desde 10/11'));
    expect(component.toneOf('c1')).toBe('error');
    expect(component.initials('Carlos Ruiz')).toBe('CR');
    expect(component.contactLine(customer as never)).toBe('CC 79 · 3104567890 · Calle 5');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    component.collect(customer as never, 'k1');
    expect(navigate).toHaveBeenCalledWith(['/operacion/creditos'], { queryParams: { cliente: 'c1', nombre: 'Carlos Ruiz', abonar: 'k1' } });
    credits.list.mockResolvedValue({ items: [{ id: 'k0', balance: 0 }, { id: 'k1', balance: 90000 }], page: 1, pageSize: 50, totalCount: 2, totalPages: 1 });
    component.openFicha(customer as never, 'creditos');
    await vi.waitFor(() => expect(component.customerCredits()?.map(c => c.id)).toEqual(['k1', 'k0']));
    expect(credits.list).toHaveBeenCalledWith({ page: 1, pageSize: 50, customerId: 'c1' });
    permissions = ['CUSTOMERS.MANAGE'];
    component.setFilter('DEBEN'); await Promise.resolve();
    expect(api.list).toHaveBeenLastCalledWith(expect.objectContaining({ credit: '' }));
  });
  it('envía alta válida normalizada', async () => {
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = ' Ana '; component.form.address = ' Calle 1 '; component.form.phone = '3001234567'; component.form.documentType = 'CC'; component.form.documentNumber = ' 123 ';
    await component.save(); expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Ana', address: 'Calle 1', documentNumber: '123' }));
  });
  it('guarda el cupo de crédito; vacío es sin límite y un valor no entero se rechaza', async () => {
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.address = 'Calle 1'; component.form.phone = '3001234567'; component.form.documentType = 'CC'; component.form.documentNumber = '123';
    component.form.creditLimit = 1500.5; await component.save();
    expect(api.create).not.toHaveBeenCalled(); expect(component.error()).toContain('cupo');
    component.form.creditLimit = '' as never; await component.save();
    expect(api.create).toHaveBeenLastCalledWith(expect.objectContaining({ creditLimit: null }));
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.address = 'Calle 1'; component.form.phone = '3001234567'; component.form.documentType = 'CC'; component.form.documentNumber = '123';
    component.form.creditLimit = 200000; await component.save();
    expect(api.create).toHaveBeenLastCalledWith(expect.objectContaining({ creditLimit: 200000 }));
  });
  it('sin CUSTOMERS.MANAGE conserva el cupo existente al editar datos básicos', async () => {
    permissions = ['CUSTOMERS.BASIC_MANAGE'];
    api.update.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    const customer = { id: 'c1', documentType: 'CC', documentNumber: '9', fullName: 'Ana', phone: null, address: 'Calle 1', email: null, observations: null, isActive: true, version: 3, creditLimit: 50000 };
    component.edit(customer); component.form.creditLimit = 1; component.form.phone = '3001234567';
    await component.save();
    expect(api.update).toHaveBeenCalledWith(customer, expect.objectContaining({ creditLimit: 50000, phone: '3001234567' }));
  });
  it('con el permiso básico también envía el documento; observaciones y cupo siguen reservados', async () => {
    permissions = ['CUSTOMERS.BASIC_MANAGE'];
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.address = 'Calle 1'; component.form.phone = '3001234567'; component.form.documentType = 'CC'; component.form.documentNumber = '123';
    component.form.documentType = 'cc'; component.form.documentNumber = ' 123 '; component.form.observations = 'privado'; component.form.creditLimit = 9000;
    await component.save();
    expect(api.create).toHaveBeenLastCalledWith(expect.objectContaining({ documentType: 'CC', documentNumber: '123', observations: null, creditLimit: null }));
  });
  it('pide confirmacion propia antes de inactivar un cliente', async () => {
    const customer = { id: 'c1', fullName: 'Ana', isActive: true, version: 2 } as never;
    api.status.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.toggle(customer);
    expect(component.statusTarget()).toBe(customer); expect(api.status).not.toHaveBeenCalled();
    await component.confirmStatusChange();
    expect(api.status).toHaveBeenCalledWith(customer); expect(component.statusTarget()).toBeNull(); expect(component.notice()).toContain('inactivado');
  });
});
