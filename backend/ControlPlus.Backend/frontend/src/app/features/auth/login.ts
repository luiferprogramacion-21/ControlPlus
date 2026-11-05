import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SessionService } from '../../core/session.service';
import { errorText } from '../../core/api';

@Component({
  selector: 'cp-login',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <main class="login-page">
      <section class="login-hero">
        <div class="brand"><span class="brand__mark">+</span><strong>ControlPlus</strong></div>
        <div class="login-hero__copy"><h1>Control diario,<br>decisiones claras.</h1>
          <p>Ventas, inventario y caja para Baratísimo.</p></div>
      </section>
      <section class="login-form-area">
        <form class="login-form" [formGroup]="form" (ngSubmit)="submit()">
          <h2>Bienvenido</h2><p class="muted">Ingresa con tu cuenta de ControlPlus.</p>
          <mat-form-field appearance="outline"><mat-label>Usuario</mat-label>
            <input matInput formControlName="userName" autocomplete="username" autofocus>
            @if (form.controls.userName.hasError('required')) { <mat-error>Escribe el usuario.</mat-error> }
          </mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Contraseña</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="current-password">
            @if (form.controls.password.hasError('required')) { <mat-error>Escribe la contraseña.</mat-error> }
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="busy()"> {{ busy() ? 'Ingresando…' : 'Ingresar' }} </button>
          @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
        </form>
      </section>
    </main>`,
  styles: [`.login-form mat-form-field { display:block; margin:16px 0 } .login-form button { width:100%; min-height:44px }`],
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
