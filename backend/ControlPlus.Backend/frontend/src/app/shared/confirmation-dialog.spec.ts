import { DestroyRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Subject } from 'rxjs';
import { ConfirmationDialog, ConfirmationDialogComponent } from './confirmation-dialog';

describe('Confirmaciones de ControlPlus', () => {
  const open = vi.fn();
  function owner() {
    const state = { destroyed: false, destroy: () => {} };
    const unregister = vi.fn();
    const ref = { get destroyed() { return state.destroyed; }, onDestroy: (callback: () => void) => {
      state.destroy = () => { state.destroyed = true; callback(); }; return unregister;
    } } as DestroyRef;
    return { ref, state, unregister };
  }
  beforeEach(() => { vi.clearAllMocks(); TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }] }); });

  it.each([true, false, undefined])('solo aceptar explícitamente aprueba (%s)', async value => {
    const closed = new Subject<boolean | undefined>(); const close = vi.fn();
    open.mockReturnValue({ afterClosed: () => closed, close });
    const { ref, unregister } = owner(); const service = TestBed.inject(ConfirmationDialog);
    const pending = service.confirm({ title: 'Eliminar usuario', message: 'Se conserva el historial.', danger: true }, ref);
    expect(open).toHaveBeenCalledWith(ConfirmationDialogComponent, expect.objectContaining({
      role: 'alertdialog', autoFocus: '#cp-confirmation-cancel', ariaModal: true,
    }));
    closed.next(value); closed.complete();
    expect(await pending).toBe(value === true); expect(unregister).toHaveBeenCalledOnce();
  });

  it('no abre dos confirmaciones en paralelo y cancela al destruir el dueño', async () => {
    const closed = new Subject<boolean>(); const close = vi.fn(value => { closed.next(value); closed.complete(); });
    open.mockReturnValue({ afterClosed: () => closed, close });
    const { ref, state } = owner(); const service = TestBed.inject(ConfirmationDialog);
    const pending = service.confirm({ title: 't', message: 'm' }, ref);
    expect(await service.confirm({ title: 'otra', message: 'm' }, ref)).toBe(false);
    state.destroy(); expect(await pending).toBe(false); expect(close).toHaveBeenCalledWith(false);
    expect(await service.confirm({ title: 't', message: 'm' }, ref)).toBe(false);
    expect(open).toHaveBeenCalledOnce();
  });

  it('cerrar una ventana padre cancela solo la confirmación que le pertenece', async () => {
    const closed = new Subject<boolean>(); const close = vi.fn(value => { closed.next(value); closed.complete(); });
    open.mockReturnValue({ afterClosed: () => closed, close });
    const { ref } = owner(); const service = TestBed.inject(ConfirmationDialog);
    const pending = service.confirm({ title: 't', message: 'm' }, ref);
    service.cancel(owner().ref); expect(close).not.toHaveBeenCalled();
    service.cancel(ref); expect(await pending).toBe(false);
  });
});
