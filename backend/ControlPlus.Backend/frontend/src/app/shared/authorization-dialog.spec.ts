import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatDialog } from '@angular/material/dialog';
import { SessionService } from '../core/session.service';
import { AuthorizationDialog, AuthorizationDialogComponent, AuthorizationPrompt } from './authorization-dialog';

describe('Ventana de autorización', () => {
  let component: AuthorizationDialogComponent;
  const close = vi.fn();
  const ref = { close, disableClose: false };
  const run = vi.fn();
  const prompt: AuthorizationPrompt = {
    title: 'Autorizar crédito', description: 'Se registrará una deuda.', detail: 'Ana · 10000 COP',
    operatorUserName: 'cajero', requireDifferentUser: true, run,
  };
  beforeEach(() => {
    vi.clearAllMocks(); ref.disableClose = false;
    TestBed.configureTestingModule({ providers: [
      { provide: MAT_DIALOG_DATA, useValue: prompt }, { provide: MatDialogRef, useValue: ref },
    ] });
    component = TestBed.runInInjectionContext(() => new AuthorizationDialogComponent());
  });

  it('cancelar limpia la contraseña y no ejecuta la operación', () => {
    component.userName = 'supervisor'; component.password = 'secreto';
    component.cancel();
    expect(run).not.toHaveBeenCalled();
    expect(component.password).toBe('');
    expect(close).toHaveBeenCalledWith(false);
  });

  it('exige otra identidad cuando el contrato lo pide', async () => {
    component.userName = ' CAJERO '; component.password = 'secreto';
    await component.submit();
    expect(run).not.toHaveBeenCalled();
    expect(component.error()).toContain('otro usuario');
    expect(component.password).toBe('');
  });

  it('bloquea el doble envío mientras espera y limpia la clave al terminar', async () => {
    let resolve!: () => void;
    run.mockReturnValue(new Promise<void>(r => { resolve = r; }));
    component.userName = 'supervisor'; component.password = 'secreto';
    const first = component.submit();
    await component.submit(); component.cancel();
    expect(run).toHaveBeenCalledOnce();
    expect(run).toHaveBeenCalledWith({ userName: 'supervisor', password: 'secreto' });
    expect(close).not.toHaveBeenCalled();
    resolve(); await first;
    expect(component.password).toBe('');
    expect(close).toHaveBeenCalledWith(true);
  });

  it('muestra rechazo dentro de la ventana y permite corregir credenciales', async () => {
    run.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { title: 'Credenciales inválidas' } })).mockResolvedValueOnce(undefined);
    component.userName = 'supervisor'; component.password = 'incorrecta';
    await component.submit();
    expect(component.error()).toContain('Credenciales inválidas');
    expect(component.password).toBe('');
    expect(close).not.toHaveBeenCalled();
    component.password = 'correcta';
    await component.submit();
    expect(close).toHaveBeenCalledWith(true);
  });
});

describe('Servicio de autorización: roles sin segundo autorizador', () => {
  const open = vi.fn();
  let roles: string[];
  const run = vi.fn();
  const base = { title: 't', description: 'd', detail: 'x', operatorUserName: 'admin', requireDifferentUser: true } as const;
  const service = () => {
    TestBed.configureTestingModule({ providers: [
      { provide: MatDialog, useValue: { open } },
      { provide: SessionService, useValue: { hasRole: (...codes: string[]) => codes.some(c => roles.includes(c)) } },
    ] });
    return TestBed.inject(AuthorizationDialog);
  };
  beforeEach(() => { vi.clearAllMocks(); roles = []; open.mockReturnValue({ afterClosed: () => ({ subscribe: (o: (v: boolean) => void) => o(true) }) }); });

  it('el rol exento ejecuta con null y no abre la ventana', async () => {
    roles = ['ADMINISTRADOR']; run.mockResolvedValue(undefined);
    expect(await service().open({ ...base, selfAuthorizedRoles: ['ADMINISTRADOR'], run })).toBe(true);
    expect(run).toHaveBeenCalledWith(null);
    expect(open).not.toHaveBeenCalled();
  });

  it('un rol no exento sigue pasando por la ventana', async () => {
    roles = ['SUPERVISOR'];
    await service().open({ ...base, selfAuthorizedRoles: ['ADMINISTRADOR'], run }).catch(() => undefined);
    expect(run).not.toHaveBeenCalled();
    expect(open).toHaveBeenCalledOnce();
  });

  it('muestra el error de la ruta sin ventana mediante onError', async () => {
    roles = ['ADMINISTRADOR']; run.mockRejectedValue(new Error('Sin permiso'));
    const onError = vi.fn();
    expect(await service().open({ ...base, selfAuthorizedRoles: ['ADMINISTRADOR'], onError, run })).toBe(false);
    expect(onError).toHaveBeenCalledWith('Sin permiso');
  });

  it('sin onError relanza el error', async () => {
    roles = ['ADMINISTRADOR']; run.mockRejectedValue(new Error('fallo'));
    await expect(service().open({ ...base, selfAuthorizedRoles: ['ADMINISTRADOR'], run })).rejects.toThrow('fallo');
  });
});
