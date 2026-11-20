import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { SessionService } from '../../core/session.service';
import { ChangePassword } from './change-password';

describe('Cambiar la contraseña por una de 4 números', () => {
  let screen: ChangePassword;
  const changeRequiredPassword = vi.fn();
  const navigateByUrl = vi.fn().mockResolvedValue(true);
  const logout = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    changeRequiredPassword.mockResolvedValue(undefined);
    TestBed.configureTestingModule({ providers: [
      { provide: SessionService, useValue: { changeRequiredPassword, logout } },
      { provide: Router, useValue: { navigateByUrl } },
    ] });
    screen = TestBed.runInInjectionContext(() => new ChangePassword());
  });

  it('exige 4 números, que coincidan y que sea distinta de la actual', async () => {
    for (const [newPassword, confirmPassword, message] of [
      ['12a4', '12a4', 'exactamente 4 números'], ['123', '123', 'exactamente 4 números'], ['12345', '12345', 'exactamente 4 números'],
      ['4821', '4822', 'no coinciden'],
    ]) {
      screen.form.setValue({ currentPassword: 'Anterior-2026', newPassword, confirmPassword });
      await screen.submit();
      expect(screen.error()).toContain(message);
    }
    screen.form.setValue({ currentPassword: '4821', newPassword: '4821', confirmPassword: '4821' });
    await screen.submit();
    expect(screen.error()).toContain('distinta');
    expect(changeRequiredPassword).not.toHaveBeenCalled();
  });

  it('cambia la contraseña y lleva al Inicio', async () => {
    screen.form.setValue({ currentPassword: 'Anterior-2026', newPassword: '4821', confirmPassword: '4821' });
    await screen.submit();
    expect(changeRequiredPassword).toHaveBeenCalledWith('Anterior-2026', '4821');
    expect(navigateByUrl).toHaveBeenCalledWith('/operacion/inicio');
  });

  it('si el servidor rechaza la contraseña actual, lo explica y se queda en la pantalla', async () => {
    changeRequiredPassword.mockRejectedValue(new Error('La contraseña actual no es correcta.'));
    screen.form.setValue({ currentPassword: 'mala', newPassword: '4821', confirmPassword: '4821' });
    await screen.submit();
    expect(screen.error()).not.toBe('');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('se puede salir sin cambiarla', () => {
    screen.logout();
    expect(logout).toHaveBeenCalledOnce();
    expect(navigateByUrl).toHaveBeenCalledWith('/ingresar');
  });
});
