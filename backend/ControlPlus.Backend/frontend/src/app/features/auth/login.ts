import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { PASSWORD_CHANGE_ROUTE, SessionService } from '../../core/session.service';
import { HttpErrorResponse } from '@angular/common/http';
import { errorText } from '../../core/api';
import { PasswordRecoveryApi } from './password-recovery-api';
import { HOME_ROUTE } from '../../core/landing';
import { toastSignal } from '../../core/toast';

/**
 * Inicio de sesión en dos paneles (aprobado por el usuario el 15 de noviembre de 2026, en lugar de la maqueta 02):
 * formulario a la izquierda y el logo de ControlPlus sobre el panel azul marino a la derecha, con los colores del logo.
 */
@Component({
  selector: 'cp-login',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <main class="cp-login">
      <section class="cp-login__form-area">
        @if (view() === 'login') {
        <form class="cp-login__form" [formGroup]="form" (ngSubmit)="submit()">
          <img class="cp-login__mark" src="controlplus-icon.png" alt="" width="64" height="64">
          <h1 class="cp-login__title">Punto de venta<br>Baratísimo</h1>
          <span class="cp-login__accent" aria-hidden="true"></span>
          <p class="cp-login__hint">Ingresa con tu usuario de ControlPlus.</p>
          <mat-form-field appearance="outline"><mat-label>Usuario</mat-label>
            <input matInput formControlName="userName" autocomplete="username" autofocus>
            @if (form.controls.userName.hasError('required')) { <mat-error>Escribe el usuario.</mat-error> }
          </mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Contraseña</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="current-password">
            @if (form.controls.password.hasError('required')) { <mat-error>Escribe la contraseña.</mat-error> }
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="busy()">{{ busy() ? 'Ingresando…' : 'Iniciar sesión' }}</button>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <button class="cp-login__link" type="button" (click)="startRecovery()">¿Olvidaste tu contraseña?</button>
          <p class="cp-login__footer">ControlPlus · sistema local de Baratísimo</p>
        </form>
        } @else {
        <form class="cp-login__form" [formGroup]="recovery" (ngSubmit)="view() === 'request' ? sendCode() : confirmCode()">
          <img class="cp-login__mark" src="controlplus-icon.png" alt="" width="64" height="64">
          <h1 class="cp-login__title">Recuperar<br>contraseña</h1>
          <span class="cp-login__accent" aria-hidden="true"></span>
          @if (view() === 'request') {
            <p class="cp-login__hint">Te enviaremos un código de 6 números al correo registrado en tu usuario. Necesita Internet.</p>
            <mat-form-field appearance="outline"><mat-label>Usuario</mat-label>
              <input matInput formControlName="userName" autocomplete="username" autofocus>
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="busy()">{{ busy() ? 'Enviando…' : 'Enviar código' }}</button>
          } @else {
            <p class="cp-login__hint">{{ notice() }}</p>
            <mat-form-field appearance="outline"><mat-label>Código del correo</mat-label>
              <input matInput formControlName="code" inputmode="numeric" maxlength="6" autocomplete="one-time-code" autofocus>
            </mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Nueva contraseña (4 números)</mat-label>
              <input matInput type="password" formControlName="newPassword" inputmode="numeric" autocomplete="new-password">
            </mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Repite la nueva contraseña</mat-label>
              <input matInput type="password" formControlName="confirmPassword" inputmode="numeric" autocomplete="new-password">
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="busy()">{{ busy() ? 'Cambiando…' : 'Cambiar contraseña' }}</button>
            <button class="cp-login__link" type="button" (click)="view.set('request'); error.set('')">No me llegó el código</button>
          }
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
          <button class="cp-login__link" type="button" (click)="backToLogin()">Volver a iniciar sesión</button>
          <p class="cp-login__footer">Si no tienes correo registrado, pide al Administrador que restablezca tu contraseña.</p>
        </form>
        }
      </section>
      <section class="cp-login__brand" aria-label="ControlPlus">
        <div class="cp-login__dots" aria-hidden="true"></div>
        <div class="cp-login__dots cp-login__dots--bottom" aria-hidden="true"></div>
        <div class="cp-login__brand-content">
          <img class="cp-login__logo" src="controlplus-logo-white.png" alt="ControlPlus" width="1000" height="191">
          <p class="cp-login__tagline">Ventas, inventario y caja en un solo lugar.</p>
        </div>
      </section>
    </main>`,
  styles: [`
    /* Colores de la marca ControlPlus (tomados del logo): azul marino y naranja. */
    :host { --brand-navy: #0F2C4D; --brand-navy-light: #1B4170; --brand-orange: #FD6D32;
      --mat-sys-primary: var(--brand-navy); --mat-sys-on-primary: #fff; display: block; }
    .cp-login { display: grid; grid-template-columns: minmax(380px, 42%) 1fr; min-height: calc(100vh / var(--cp-zoom)); background: #fff; }
    .cp-login__form-area { display: grid; place-items: center; padding: 40px 28px; background: #fff; }
    .cp-login__form { display: flex; flex-direction: column; align-items: stretch; width: min(100%, 340px); text-align: center; }
    .cp-login__mark { align-self: center; width: 64px; height: 64px; margin-bottom: 14px; }
    .cp-login__title { margin: 0; color: var(--brand-navy); font-size: 26px; font-weight: 700; line-height: 1.2;
      letter-spacing: .03em; text-transform: uppercase; }
    .cp-login__accent { align-self: center; width: 48px; height: 4px; margin: 12px 0 10px; border-radius: 2px; background: var(--brand-orange); }
    .cp-login__hint { margin: 0 0 18px; color: #5b6b80; font-size: 14px; }
    .cp-login__form mat-form-field { display: block; margin: 10px 0; text-align: left; }
    .cp-login__form button { width: 100%; min-height: 46px; margin-top: 6px; border-radius: 10px;
      background: var(--brand-navy); color: #fff; font-weight: 600; letter-spacing: .02em; }
    .cp-login__form button:not([disabled]):hover { background: var(--brand-navy-light); }
    .cp-login__footer { margin: 28px 0 0; color: #7a8796; font-size: 12px; }
    .cp-login__form .cp-login__link { width: auto; min-height: 0; margin: 14px auto 0; padding: 4px 8px; border: 0; border-radius: 6px;
      background: transparent; color: var(--brand-navy); font: inherit; font-size: 14px; font-weight: 600; text-decoration: underline; cursor: pointer; }
    .cp-login__form .cp-login__link:hover { background: #eef2f7; }
    .cp-login__notice { margin: 12px 0 0; color: #1d6b3a; font-size: 14px; }
    .cp-login__brand { position: relative; display: grid; place-items: center; padding: 56px; overflow: hidden;
      background: radial-gradient(circle at 70% 30%, var(--brand-navy-light) 0%, var(--brand-navy) 62%); }
    .cp-login__dots { position: absolute; top: 34px; right: 40px; width: 150px; height: 70px; opacity: .35;
      background-image: radial-gradient(circle, #fff 1.6px, transparent 2px); background-size: 16px 16px; }
    .cp-login__dots--bottom { top: auto; right: auto; bottom: 34px; left: 40px; opacity: .55;
      background-image: radial-gradient(circle, var(--brand-orange) 1.6px, transparent 2px); }
    .cp-login__brand-content { width: min(100%, 520px); text-align: center; }
    .cp-login__logo { display: block; width: 100%; height: auto; }
    .cp-login__tagline { margin: 22px 0 0; color: #c9d6e6; font-size: 16px; letter-spacing: .02em; }
    @container cp (max-width: 820px) {
      .cp-login { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
      .cp-login__brand { order: -1; padding: 32px 24px; }
      .cp-login__dots { display: none; }
      .cp-login__brand-content { width: min(100%, 320px); }
      .cp-login__tagline { display: none; }
      .cp-login__mark { display: none; }
    }
  `],
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly recoveryApi = inject(PasswordRecoveryApi);
  readonly busy = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly view = signal<'login' | 'request' | 'confirm'>('login');
  readonly form = this.fb.nonNullable.group({ userName: ['', Validators.required], password: ['', Validators.required] });
  readonly recovery = this.fb.nonNullable.group({ userName: [''], code: [''], newPassword: [''], confirmPassword: [''] });

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) { this.form.markAllAsTouched(); return; }
    this.busy.set(true); this.error.set(''); this.notice.set('');
    try {
      const { userName, password } = this.form.getRawValue();
      await this.session.login(userName.trim(), password);
      await this.router.navigateByUrl(this.session.passwordChangeRequired() ? PASSWORD_CHANGE_ROUTE : HOME_ROUTE);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.busy.set(false); }
  }

  startRecovery(): void {
    this.error.set(''); this.notice.set('');
    this.recovery.reset({ userName: this.form.controls.userName.value.trim(), code: '', newPassword: '', confirmPassword: '' });
    this.view.set('request');
  }

  backToLogin(): void {
    this.error.set(''); this.recovery.reset(); this.view.set('login');
  }

  async sendCode(): Promise<void> {
    const userName = this.recovery.controls.userName.value.trim();
    if (!userName) { this.error.set('Escribe tu usuario.'); return; }
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.recoveryApi.request(userName);
      this.notice.set(result.message); this.view.set('confirm');
    } catch (e) { this.error.set(this.recoveryError(e)); }
    finally { this.busy.set(false); }
  }

  async confirmCode(): Promise<void> {
    const { userName, code, newPassword, confirmPassword } = this.recovery.getRawValue();
    if (!/^[0-9]{6}$/.test(code.trim())) { this.error.set('El código tiene 6 números.'); return; }
    if (!newPassword) { this.error.set('Escribe la nueva contraseña.'); return; }
    if (newPassword !== confirmPassword) { this.error.set('Las contraseñas no coinciden.'); return; }
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      await this.recoveryApi.confirm(userName.trim(), code.trim(), newPassword);
      this.form.reset({ userName: userName.trim(), password: '' });
      this.recovery.reset(); this.view.set('login');
      this.notice.set('Contraseña cambiada. Ingresa con la nueva.');
    } catch (e) { this.error.set(this.recoveryError(e)); }
    finally { this.busy.set(false); }
  }

  /** 503: correo no configurado o sin conexión (el servidor explica qué hacer); 429: demasiados intentos. */
  private recoveryError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 503 && typeof error.error?.title === 'string') return error.error.title;
    if (error instanceof HttpErrorResponse && error.status === 429) return 'Demasiados intentos. Espera un minuto y vuelve a intentarlo.';
    return errorText(error);
  }

}
