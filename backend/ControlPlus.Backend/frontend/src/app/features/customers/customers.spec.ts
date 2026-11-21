import { TestBed } from '@angular/core/testing';
import { Customers } from './customers';
import { CustomersApi } from './customers-api';
import { SessionService } from '../../core/session.service';

describe('Customers', () => {
  let component: Customers;
  const api = { list: vi.fn(), create: vi.fn(), update: vi.fn(), status: vi.fn(), get: vi.fn() };
  let permissions = ['CUSTOMERS.MANAGE'];
  const session = { has: (permission: string) => permissions.includes(permission) };
  beforeEach(() => { vi.clearAllMocks(); permissions = ['CUSTOMERS.MANAGE']; TestBed.configureTestingModule({ providers: [{ provide: CustomersApi, useValue: api }, { provide: SessionService, useValue: session }] }); component = TestBed.runInInjectionContext(() => new Customers()); });
  it('valida la pareja documental antes del alta; la dirección es opcional (21 de noviembre de 2026)', async () => {
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.documentType = 'CC';
    await component.save(); expect(api.create).not.toHaveBeenCalled();
    component.form.address = 'x'.repeat(301); component.form.documentType = null;
    await component.save(); expect(api.create).not.toHaveBeenCalled();
  });
  it('envía alta válida normalizada', async () => {
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = ' Ana '; component.form.address = ' Calle 1 '; component.form.documentType = 'CC'; component.form.documentNumber = ' 123 ';
    await component.save(); expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Ana', address: 'Calle 1', documentNumber: '123' }));
  });
  it('guarda el cupo de crédito; vacío es sin límite y un valor no entero se rechaza', async () => {
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.address = 'Calle 1';
    component.form.creditLimit = 1500.5; await component.save();
    expect(api.create).not.toHaveBeenCalled(); expect(component.error()).toContain('cupo');
    component.form.creditLimit = '' as never; await component.save();
    expect(api.create).toHaveBeenLastCalledWith(expect.objectContaining({ creditLimit: null }));
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.address = 'Calle 1';
    component.form.creditLimit = 200000; await component.save();
    expect(api.create).toHaveBeenLastCalledWith(expect.objectContaining({ creditLimit: 200000 }));
  });
  it('sin CUSTOMERS.MANAGE conserva el cupo existente al editar datos básicos', async () => {
    permissions = ['CUSTOMERS.BASIC_MANAGE'];
    api.update.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    const customer = { id: 'c1', documentType: null, documentNumber: null, fullName: 'Ana', phone: null, address: 'Calle 1', email: null, observations: null, isActive: true, version: 3, creditLimit: 50000 };
    component.edit(customer); component.form.creditLimit = 1; component.form.phone = '300';
    await component.save();
    expect(api.update).toHaveBeenCalledWith(customer, expect.objectContaining({ creditLimit: 50000, phone: '300' }));
  });
  it('con el permiso básico también envía el documento; observaciones y cupo siguen reservados', async () => {
    permissions = ['CUSTOMERS.BASIC_MANAGE'];
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = 'Ana'; component.form.address = 'Calle 1';
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
