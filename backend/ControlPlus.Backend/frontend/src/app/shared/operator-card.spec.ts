import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { CashApi } from '../features/cash/cash-api';
import { OperatorCard, OperatorCardDialogComponent } from './operator-card';

const shift = { id: 'shift-1', mode: 'COMPARTIDO', state: 'ABIERTA', operatingDate: '2026-11-25', openedAtUtc: '', initialAmount: 0, version: 1, responsibleUserId: null };
const sharedState = { isConfigured: true, hasOpenShift: true, defaultShiftMode: 'COMPARTIDO', requiresPreviousShiftClosure: false,
  requiresOperatorSession: true, guidance: null, currentShift: shift, ownOperatorSession: null };

describe('Carné de quien opera en turno compartido (pruebas del usuario del 25/11/2026)', () => {
  const open = vi.fn();
  const api = { state: vi.fn(), startSession: vi.fn() };
  const card = () => {
    TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }, { provide: CashApi, useValue: api }] });
    return TestBed.inject(OperatorCard);
  };
  beforeEach(() => vi.clearAllMocks());

  it('en turno individual no pide carné', async () => {
    api.state.mockResolvedValue({ ...sharedState, requiresOperatorSession: false });
    expect(await card().ensure('Ingreso')).toEqual({ shared: false, role: null, sessionId: null });
    expect(open).not.toHaveBeenCalled();
  });

  it('con un carné ya pasado sigue a nombre de su dueño sin preguntar', async () => {
    api.state.mockResolvedValue({ ...sharedState, ownOperatorSession: { id: 's1', state: 'ACTIVA' }, activeOperatorRole: 'CAJERO' });
    expect(await card().ensure('Ingreso')).toEqual({ shared: true, role: 'CAJERO', sessionId: 's1' });
    expect(open).not.toHaveBeenCalled();
  });

  it('si nadie ha pasado su carné lo pide y sigue con el rol de su dueño', async () => {
    api.state.mockResolvedValueOnce(sharedState)
      .mockResolvedValueOnce({ ...sharedState, ownOperatorSession: { id: 's2', state: 'ACTIVA' }, activeOperatorRole: 'SUPERVISOR' });
    open.mockReturnValue({ afterClosed: () => of(true) });
    expect(await card().ensure('Retiro de efectivo de 100.000 COP')).toEqual({ shared: true, role: 'SUPERVISOR', sessionId: 's2' });
    expect(open).toHaveBeenCalledWith(OperatorCardDialogComponent, expect.objectContaining({ data: { detail: 'Retiro de efectivo de 100.000 COP' } }));
  });

  it('si se cancela devuelve null', async () => {
    api.state.mockResolvedValue(sharedState);
    open.mockReturnValue({ afterClosed: () => of(false) });
    expect(await card().ensure('Ingreso')).toBeNull();
  });

  it('si no se puede consultar la caja deja decidir al servidor', async () => {
    api.state.mockRejectedValue(new Error('sin conexión'));
    expect(await card().ensure()).toEqual({ shared: false, role: null, sessionId: null });
  });
});

describe('Ventana «Pasa tu carné»', () => {
  const close = vi.fn();
  const ref = { close, disableClose: false, keydownEvents: () => of() };
  const api = { startSession: vi.fn(), startSessionWithPassword: vi.fn() };
  const create = () => {
    TestBed.configureTestingModule({ providers: [
      { provide: MAT_DIALOG_DATA, useValue: { detail: 'Ingreso de 5.000 COP' } },
      { provide: MatDialogRef, useValue: ref }, { provide: CashApi, useValue: api },
    ] });
    const fixture = TestBed.createComponent(OperatorCardDialogComponent);
    fixture.detectChanges();
    return fixture;
  };
  beforeEach(() => vi.clearAllMocks());

  it('explica para qué es y abre la sesión con el carné', async () => {
    const fixture = create(); const component = fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('la operación queda a nombre de quien pasa su carné');
    expect(fixture.nativeElement.textContent).toContain('Ingreso de 5.000 COP');
    api.startSession.mockResolvedValue({ id: 's1', state: 'ACTIVA' });
    component.token = ' 12345678901234 ';
    await component.submit();
    expect(api.startSession).toHaveBeenCalledWith('12345678901234');
    expect(close).toHaveBeenCalledWith(true);
  });

  it('un carné inválido se explica y no cierra la ventana', async () => {
    const component = create().componentInstance;
    api.startSession.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    component.token = '999';
    await component.submit();
    expect(component.error()).toContain('El carné no es válido');
    expect(component.token).toBe('');
    expect(close).not.toHaveBeenCalled();
  });

  it('sin carné, quien opera escribe su usuario y contraseña (25/11/2026)', async () => {
    const fixture = create(); const component = fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('o escribe tu usuario y tu contraseña');
    api.startSessionWithPassword.mockResolvedValue({ id: 's1', state: 'ACTIVA' });
    component.userName = ' cajera '; component.password = '1234';
    await component.submit();
    expect(api.startSessionWithPassword).toHaveBeenCalledWith('cajera', '1234');
    expect(api.startSession).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalledWith(true);
    expect(component.password).toBe('');
  });

  it('una contraseña errada se explica y recuerda el bloqueo', async () => {
    const component = create().componentInstance;
    api.startSessionWithPassword.mockRejectedValue(new HttpErrorResponse({ status: 401, error: { title: 'Usuario o contraseña incorrectos.' } }));
    component.userName = 'cajera'; component.password = '0000';
    await component.submit();
    expect(component.error()).toContain('Usuario o contraseña incorrectos.');
    expect(component.error()).toContain('5 intentos fallidos');
    expect(close).not.toHaveBeenCalled();
  });

  it('si el usuario no tiene carné explica los pasos y quién los hace (25/11/2026)', async () => {
    const fixture = create(); const component = fixture.componentInstance;
    api.startSessionWithPassword.mockRejectedValue(new HttpErrorResponse({ status: 409,
      error: { code: 'operator.card_missing', title: 'Ese usuario no tiene un carné vigente.' } }));
    component.userName = 'cajera'; component.password = '1234';
    await component.submit();
    fixture.detectChanges();
    expect(component.missingCard()).toBe(true);
    expect(component.error()).toBe('Ese usuario no tiene un carné vigente.');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('lo hace el Administrador');
    expect(text).toContain('Abrir ficha');
    expect(text).toContain('Emitir carné');
    expect(close).not.toHaveBeenCalled();
  });

  it('sin carné pide pasarlo y cancelar cierra sin abrir sesión', async () => {
    const component = create().componentInstance;
    await component.submit();
    expect(component.error()).toBe('Pasa tu carné por el lector, o escribe tu usuario y tu contraseña.');
    component.cancel();
    expect(api.startSession).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalledWith(false);
  });
});
