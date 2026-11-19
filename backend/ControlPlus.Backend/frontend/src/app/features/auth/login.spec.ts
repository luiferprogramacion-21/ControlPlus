import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { Login } from './login';
import { SessionService } from '../../core/session.service';
import { CashApi } from '../cash/cash-api';
import { HttpErrorResponse } from '@angular/common/http';
import { PasswordRecoveryApi } from './password-recovery-api';

describe('Login: pantalla de llegada', () => {
  const navigateByUrl = vi.fn().mockResolvedValue(true);
  const state = vi.fn();
  const recovery = { request: vi.fn(), confirm: vi.fn() };
  let role = 'CAJERO';
  let permissions: string[] = [];
  const session = {
    login: vi.fn().mockResolvedValue(undefined),
    passwordChangeRequired: vi.fn().mockReturnValue(false),
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
      { provide: CashApi, useValue: { state } }, { provide: PasswordRecoveryApi, useValue: recovery },
    ] });
  });

  it('todos llegan a Inicio, que muestra las tareas del rol y el estado de la caja', async () => {
    for (const [code, granted] of [['CAJERO', ['SALES.CREATE', 'CASH.OWN_READ']], ['ADMINISTRADOR', ['REPORTS.FINANCIAL_READ']]] as const) {
      role = code; permissions = [...granted];
      await submit();
      expect(navigateByUrl).toHaveBeenLastCalledWith('/operacion/inicio');
    }
    expect(session.login).toHaveBeenCalledWith('cajero', 'clave');
    expect(state).not.toHaveBeenCalled();
  });

  it('quien entra con una contraseña que no es de 4 números va a cambiarla', async () => {
    session.passwordChangeRequired.mockReturnValueOnce(true);
    await submit();
    expect(navigateByUrl).toHaveBeenLastCalledWith('/cambiar-contrasena');
  });

  it('si el inicio de sesión falla no navega y muestra el error', async () => {
    session.login.mockRejectedValueOnce(new Error('credenciales'));
    const component = await submit();
    expect(navigateByUrl).not.toHaveBeenCalled();
    expect(component.error()).not.toBe('');
  });
  describe('¿Olvidaste tu contraseña?', () => {
    const open = () => {
      const component = TestBed.createComponent(Login).componentInstance;
      component.form.controls.userName.setValue(' cajera ');
      component.startRecovery();
      return component;
    };

    it('envía el código con el usuario ya escrito y luego cambia la contraseña', async () => {
      recovery.request.mockResolvedValue({ message: 'Si el usuario tiene un correo registrado, le enviamos un código.' });
      recovery.confirm.mockResolvedValue(undefined);
      const component = open();
      expect(component.view()).toBe('request');
      expect(component.recovery.controls.userName.value).toBe('cajera');
      await component.sendCode();
      expect(recovery.request).toHaveBeenCalledWith('cajera');
      expect(component.view()).toBe('confirm');
      expect(component.notice()).toContain('le enviamos un código');

      component.recovery.patchValue({ code: '12345', newPassword: 'nueva', confirmPassword: 'nueva' });
      await component.confirmCode();
      expect(component.error()).toBe('El código tiene 6 números.');
      component.recovery.patchValue({ code: '123456', confirmPassword: 'otra' });
      await component.confirmCode();
      expect(component.error()).toBe('Las contraseñas no coinciden.');
      expect(recovery.confirm).not.toHaveBeenCalled();

      component.recovery.patchValue({ confirmPassword: 'nueva' });
      await component.confirmCode();
      expect(recovery.confirm).toHaveBeenCalledWith('cajera', '123456', 'nueva');
      expect(component.view()).toBe('login');
      expect(component.notice()).toBe('Contraseña cambiada. Ingresa con la nueva.');
      expect(component.form.controls.userName.value).toBe('cajera');
      expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('si el correo no está configurado o no hay Internet muestra lo que dice el servidor', async () => {
      recovery.request.mockRejectedValue(new HttpErrorResponse({ status: 503,
        error: { title: 'La recuperación por correo no está configurada. Pida al Administrador que restablezca su contraseña.' } }));
      const component = open();
      await component.sendCode();
      expect(component.view()).toBe('request');
      expect(component.error()).toContain('Pida al Administrador');
    });

    it('un código equivocado deja el error del servidor y demasiados intentos piden esperar', async () => {
      recovery.request.mockResolvedValue({ message: 'enviado' });
      const component = open();
      await component.sendCode();
      recovery.confirm.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { title: 'El código no es válido o ya venció. Pida uno nuevo.' } }));
      component.recovery.patchValue({ code: '000000', newPassword: 'nueva', confirmPassword: 'nueva' });
      await component.confirmCode();
      expect(component.error()).toContain('no es válido');
      recovery.confirm.mockRejectedValueOnce(new HttpErrorResponse({ status: 429 }));
      await component.confirmCode();
      expect(component.error()).toContain('Espera un minuto');
      component.backToLogin();
      expect(component.view()).toBe('login');
    });
  });
});
