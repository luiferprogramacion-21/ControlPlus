import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { Login } from './login';
import { SessionService } from '../../core/session.service';
import { CashApi } from '../cash/cash-api';

describe('Login: pantalla de llegada', () => {
  const navigateByUrl = vi.fn().mockResolvedValue(true);
  const state = vi.fn();
  let role = 'CAJERO';
  let permissions: string[] = [];
  const session = {
    login: vi.fn().mockResolvedValue(undefined),
    has: (permission: string) => permissions.includes(permission),
    hasRole: (...codes: string[]) => codes.includes(role),
  };
  const submit = async () => {
    const component = TestBed.createComponent(Login).componentInstance;
    component.form.setValue({ userName: ' cajero ', password: 'clave' });
    await component.submit();
    return component;
  };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ imports: [Login], providers: [
      { provide: SessionService, useValue: session }, { provide: Router, useValue: { navigateByUrl } },
      { provide: CashApi, useValue: { state } },
    ] });
  });

  it('el Cajero sin turno abierto llega a Caja', async () => {
    role = 'CAJERO'; permissions = ['SALES.CREATE', 'CASH.OWN_READ'];
    state.mockResolvedValue({ hasOpenShift: false, requiresPreviousShiftClosure: false });
    await submit();
    expect(session.login).toHaveBeenCalledWith('cajero', 'clave');
    expect(navigateByUrl).toHaveBeenCalledWith('/operacion/caja');
  });

  it('el Cajero con turno abierto llega al punto de venta; si Caja no responde, también', async () => {
    role = 'CAJERO'; permissions = ['SALES.CREATE', 'CASH.OWN_READ'];
    state.mockResolvedValueOnce({ hasOpenShift: true, requiresPreviousShiftClosure: false });
    await submit();
    expect(navigateByUrl).toHaveBeenLastCalledWith('/operacion/ventas');
    state.mockRejectedValueOnce(new Error('sin respuesta'));
    await submit();
    expect(navigateByUrl).toHaveBeenLastCalledWith('/operacion/ventas');
  });

  it('el Administrador llega al Dashboard sin consultar Caja', async () => {
    role = 'ADMINISTRADOR'; permissions = ['REPORTS.FINANCIAL_READ', 'SALES.CREATE', 'CASH.OWN_READ'];
    await submit();
    expect(state).not.toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/operacion/dashboard');
  });

  it('si el inicio de sesión falla no navega y muestra el error', async () => {
    session.login.mockRejectedValueOnce(new Error('credenciales'));
    const component = await submit();
    expect(navigateByUrl).not.toHaveBeenCalled();
    expect(component.error()).not.toBe('');
  });
});
