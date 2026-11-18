import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** «¿Olvidaste tu contraseña?»: código de 6 dígitos al correo del usuario (decisión del 18 de noviembre de 2026). */
@Injectable({ providedIn: 'root' })
export class PasswordRecoveryApi {
  private readonly http = inject(HttpClient);

  /** Respuesta igual exista o no el usuario; 503 si el correo saliente no está configurado o no se pudo enviar. */
  request(userName: string): Promise<{ message: string }> {
    return firstValueFrom(this.http.post<{ message: string }>('/api/auth/password-recovery', { userName }));
  }

  confirm(userName: string, code: string, newPassword: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/password-recovery/confirm', { userName, code, newPassword }));
  }
}
