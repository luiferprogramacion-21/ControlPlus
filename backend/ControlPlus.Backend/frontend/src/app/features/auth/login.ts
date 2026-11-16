import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SessionService } from '../../core/session.service';
import { errorText } from '../../core/api';

/**
 * Inicio de sesión en dos paneles (aprobado por el usuario el 15 de noviembre de 2026, en lugar de la maqueta 02):
 * formulario a la izquierda y el logo de ControlPlus sobre el panel azul a la derecha.
 */
@Component({
  selector: 'cp-login',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <main class="cp-login">
      <section class="cp-login__form-area">
        <form class="cp-login__form" [formGroup]="form" (ngSubmit)="submit()">
          <h1 class="cp-login__title">Punto de venta<br>Baratísimo</h1>
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
          <p class="cp-login__footer">ControlPlus · sistema local de Baratísimo</p>
        </form>
      </section>
      <section class="cp-login__brand" aria-label="ControlPlus">
        <div class="cp-login__dots" aria-hidden="true"></div>
        <div class="cp-login__card"><img src="controlplus-logo.png" alt="ControlPlus" width="900" height="172"></div>
      </section>
    </main>`,
  styles: [`
    :host { display: block; }
    .cp-login { display: grid; grid-template-columns: minmax(380px, 42%) 1fr; min-height: 100vh; background: #fff; }
    .cp-login__form-area { display: grid; place-items: center; padding: 40px 28px; background: #fff; }
    .cp-login__form { width: min(100%, 340px); text-align: center; }
    .cp-login__title { margin: 0 0 8px; color: var(--cp-primary); font-size: 26px; font-weight: 600; line-height: 1.2; letter-spacing: .02em; text-transform: uppercase; }
    .cp-login__hint { margin: 0 0 18px; color: var(--cp-text-muted); font-size: 14px; }
    .cp-login__form mat-form-field { display: block; margin: 10px 0; text-align: left; }
    .cp-login__form button { width: 100%; min-height: 44px; margin-top: 6px; }
    .cp-login__footer { margin: 28px 0 0; color: var(--cp-text-muted); font-size: 12px; }
    .cp-login__brand { position: relative; display: grid; place-items: center; padding: 48px; overflow: hidden;
      background: var(--cp-primary); }
    .cp-login__dots { position: absolute; top: 34px; right: 40px; width: 150px; height: 70px; opacity: .45;
      background-image: radial-gradient(circle, #fff 1.6px, transparent 2px); background-size: 16px 16px; }
    .cp-login__card { width: min(100%, 560px); padding: 34px 38px; border: 4px solid #fff; border-radius: 26px;
      background: #fff; box-shadow: 0 18px 40px rgba(0, 0, 0, .18); }
    .cp-login__card img { display: block; width: 100%; height: auto; }
    @media (max-width: 820px) {
      .cp-login { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
      .cp-login__brand { order: -1; padding: 28px 20px; }
      .cp-login__dots { display: none; }
      .cp-login__card { width: min(100%, 360px); padding: 18px 22px; border-radius: 18px; }
    }
  `],
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly form = this.fb.nonNullable.group({ userName: ['', Validators.required], password: ['', Validators.required] });

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) { this.form.markAllAsTouched(); return; }
    this.busy.set(true); this.error.set('');
    try {
      const { userName, password } = this.form.getRawValue();
      await this.session.login(userName.trim(), password);
      await this.router.navigateByUrl('/operacion/catalogo');
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.busy.set(false); }
  }
}
