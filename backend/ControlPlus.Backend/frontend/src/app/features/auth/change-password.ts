import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { errorText } from '../../core/api';
import { HOME_ROUTE } from '../../core/landing';
import { SessionService } from '../../core/session.service';

/** Una contraseña de 4 números: solo dígitos. */
export const PIN_PATTERN = /^\d{4}$/;

/**
 * Contraseñas de 4 números (decisión del 19 de noviembre de 2026, para todos los roles). Quien entra con una contraseña
 * anterior llega aquí y no puede usar el sistema hasta cambiarla.
 */
@Component({
  selector: 'cp-change-password',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <main class="cp-login">
      <section class="cp-login__form-area">
        <form class="cp-login__form" [formGroup]="form" (ngSubmit)="submit()">
          <img class="cp-login__mark" src="controlplus-icon.png" alt="" width="64" height="64">
          <h1 class="cp-login__title">Cambia tu<br>contraseña</h1>
          <span class="cp-login__accent" aria-hidden="true"></span>
          <p class="cp-login__hint">Ahora la contraseña es de <strong>4 números</strong>, por ejemplo 4821. Elige una que no sea fácil de adivinar (no uses 1234 ni 0000) y no la compartas.</p>
          <mat-form-field appearance="outline"><mat-label>Contraseña actual</mat-label>
              <input matInput type="password" formControlName="currentPassword" autocomplete="off" autofocus>
          </mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Nueva contraseña (4 números)</mat-label>
              <input matInput type="password" formControlName="newPassword" inputmode="numeric" maxlength="4" autocomplete="off">
          </mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Repite la nueva contraseña</mat-label>
              <input matInput type="password" formControlName="confirmPassword" inputmode="numeric" maxlength="4" autocomplete="off">
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="busy()">{{ busy() ? 'Cambiando…' : 'Cambiar contraseña' }}</button>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <button class="cp-login__link" type="button" (click)="logout()">Salir sin cambiarla</button>
          <p class="cp-login__footer">Si no recuerdas tu contraseña actual, pide al Administrador que la restablezca.</p>
        </form>
      </section>
    </main>`,
})
export class ChangePassword {
  private readonly fb = inject(FormBuilder);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', Validators.required], newPassword: ['', Validators.required], confirmPassword: ['', Validators.required],
  });

  validation(): string {
    const { currentPassword, newPassword, confirmPassword } = this.form.getRawValue();
    if (!currentPassword) return 'Escribe tu contraseña actual.';
    if (!PIN_PATTERN.test(newPassword)) return 'La nueva contraseña debe tener exactamente 4 números.';
    if (newPassword !== confirmPassword) return 'Las dos contraseñas nuevas no coinciden.';
    if (newPassword === currentPassword) return 'La nueva contraseña debe ser distinta de la actual.';
    return '';
  }

  async submit(): Promise<void> {
    if (this.busy()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.busy.set(true); this.error.set('');
    try {
      const { currentPassword, newPassword } = this.form.getRawValue();
      await this.session.changeRequiredPassword(currentPassword, newPassword);
      await this.router.navigateByUrl(HOME_ROUTE);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.busy.set(false); }
  }

  logout(): void { this.session.logout(); void this.router.navigateByUrl('/ingresar'); }
}
