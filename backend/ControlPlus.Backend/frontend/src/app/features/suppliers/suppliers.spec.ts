import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Subject } from 'rxjs';
import { SessionService } from '../../core/session.service';
import { ConfirmationDialogComponent } from '../../shared/confirmation-dialog';
import { Suppliers } from './suppliers';
import { Supplier, SuppliersApi } from './suppliers-api';

describe('Proveedores: confirmación de cambio de estado', () => {
  const supplier: Supplier = { id: 's1', name: 'Distribuidora', documentType: 'NIT', documentNumber: '00123',
    phone: null, address: null, email: null, observations: null, isActive: true, version: 3 };
  const api = { status: vi.fn(), list: vi.fn() };
  const open = vi.fn();
  let canManage: boolean;
  let component: Suppliers;
  let closed: Subject<boolean | undefined>;

  beforeEach(() => {
    vi.clearAllMocks(); canManage = true;
    closed = new Subject<boolean | undefined>();
    open.mockReturnValue({ afterClosed: () => closed, close: (value: boolean) => { closed.next(value); closed.complete(); } });
    api.status.mockResolvedValue({ ...supplier, isActive: false, version: 4 });
    api.list.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    TestBed.configureTestingModule({ providers: [
      { provide: SuppliersApi, useValue: api },
      { provide: SessionService, useValue: { has: () => canManage } },
      { provide: MatDialog, useValue: { open } },
    ] });
    component = TestBed.runInInjectionContext(() => new Suppliers());
  });

  it.each([false, undefined])('cancelar o cerrar no cambia el proveedor (%s)', async decision => {
    const pending = component.toggle(supplier);
    expect(api.status).not.toHaveBeenCalled();
    expect(open).toHaveBeenCalledWith(ConfirmationDialogComponent, expect.objectContaining({ data: expect.objectContaining({
      title: 'Inactivar proveedor', message: '¿Inactivar a Distribuidora?', danger: true,
    }) }));
    closed.next(decision); closed.complete(); await pending;
    expect(api.status).not.toHaveBeenCalled();
  });

  it('solo confirma una vez aunque se intente abrir dos avisos', async () => {
    const pending = component.toggle(supplier);
    await component.toggle(supplier);
    expect(open).toHaveBeenCalledOnce(); expect(api.status).not.toHaveBeenCalled();
    closed.next(true); closed.complete(); await pending;
    expect(api.status).toHaveBeenCalledExactlyOnceWith(supplier);
    expect(api.list).toHaveBeenCalledOnce();
  });

  it('no ejecuta una confirmación que perdió el permiso mientras estaba abierta', async () => {
    const pending = component.toggle(supplier); canManage = false;
    closed.next(true); closed.complete(); await pending;
    expect(api.status).not.toHaveBeenCalled();
  });

  it('destruir la pantalla cancela la operación pendiente', async () => {
    const pending = component.toggle(supplier);
    TestBed.resetTestingModule(); await pending;
    expect(api.status).not.toHaveBeenCalled();
  });
});
