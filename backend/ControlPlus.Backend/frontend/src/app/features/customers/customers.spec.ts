import { TestBed } from '@angular/core/testing';
import { Customers } from './customers';
import { CustomersApi } from './customers-api';
import { SessionService } from '../../core/session.service';

describe('Customers', () => {
  let component: Customers;
  const api = { list: vi.fn(), create: vi.fn(), update: vi.fn(), status: vi.fn(), get: vi.fn() };
  const session = { has: (permission: string) => permission === 'CUSTOMERS.MANAGE' };
  beforeEach(() => { vi.clearAllMocks(); TestBed.configureTestingModule({ providers: [{ provide: CustomersApi, useValue: api }, { provide: SessionService, useValue: session }] }); component = TestBed.runInInjectionContext(() => new Customers()); });
  it('valida dirección y pareja documental antes del alta', async () => {
    component.newCustomer(); component.form.fullName = 'Ana';
    await component.save(); expect(api.create).not.toHaveBeenCalled();
    component.form.address = 'Calle 1'; component.form.documentType = 'CC';
    await component.save(); expect(api.create).not.toHaveBeenCalled();
  });
  it('envía alta válida normalizada', async () => {
    api.create.mockResolvedValue({}); api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    component.newCustomer(); component.form.fullName = ' Ana '; component.form.address = ' Calle 1 '; component.form.documentType = 'CC'; component.form.documentNumber = ' 123 ';
    await component.save(); expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Ana', address: 'Calle 1', documentNumber: '123' }));
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
