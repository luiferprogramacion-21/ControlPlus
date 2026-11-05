import { Component, inject, Injectable, OnDestroy, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { errorText } from '../core/api';
import { SessionService } from '../core/session.service';

export interface AuthorizationCredentials { userName: string; password: string; }
export interface AuthorizationPrompt {
  title: string;
  description: string;
  detail: string;
  operatorUserName: string;
  requireDifferentUser: boolean;
  /** Roles that run the operation without a second authorizer (run receives null). Business limits and audit still apply on the server. */
  selfAuthorizedRoles?: string[];
  /** Shows errors of the dialog-less path; without it they are rethrown. */
  onError?: (message: string) => void;
  run: (credentials: AuthorizationCredentials | null) => Promise<void>;
}

@Component({
  selector: 'cp-authorization-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule],
  template: `
    <div class="authorization-dialog">
      <h2 mat-dialog-title>{{ data.title }}</h2>
      <mat-dialog-content>
        <p>{{ data.description }}</p><p><strong>{{ data.detail }}</strong></p>
        <form id="authorization-form" (ngSubmit)="submit()">
          <label for="authorization-user">Usuario autorizador</label>
          <input id="authorization-user" class="text-field" name="user" [(ngModel)]="userName" autocomplete="off" required [disabled]="busy()">
          <label for="authorization-password">Contraseña del autorizador</label>
          <input id="authorization-password" class="text-field" type="password" name="password" [(ngModel)]="password" autocomplete="off" required [disabled]="busy()">
        </form>
        @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
        @if (busy()) { <p role="status">Procesando autorización…</p> }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" (click)="cancel()" [disabled]="busy()">Cancelar</button>
        <button mat-flat-button type="submit" form="authorization-form" [disabled]="busy()">{{ busy() ? 'Procesando…' : 'Autorizar y continuar' }}</button>
      </mat-dialog-actions>
    </div>`,
  styles: [`
    .authorization-dialog { min-width:min(420px, calc(100vw - 48px)); max-width:560px; }
    mat-dialog-content { display:grid; gap:10px; }
    mat-dialog-content p { margin:0; line-height:1.5; }
    label { font-weight:600; margin-top:6px; }
    .text-field { width:100%; box-sizing:border-box; }
    .field-error { color:var(--cp-error); }
  `],
})
export class AuthorizationDialogComponent implements OnDestroy {
  readonly data = inject<AuthorizationPrompt>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<AuthorizationDialogComponent>);
  readonly busy = signal(false);
  readonly error = signal('');
  userName = '';
  password = '';

  ngOnDestroy(): void { this.password = ''; }

  cancel(): void { if (this.busy()) return; this.password = ''; this.ref.close(false); }
  async submit(): Promise<void> {
    if (this.busy()) return;
    const userName = this.userName.trim();
    if (!userName || !this.password) { this.error.set('Ingresa usuario y contraseña del autorizador.'); return; }
    if (this.data.requireDifferentUser && userName.toLocaleLowerCase() === this.data.operatorUserName.trim().toLocaleLowerCase()) {
      this.error.set('El autorizador debe ser otro usuario.'); this.password = ''; return;
    }
    this.busy.set(true); this.error.set(''); this.ref.disableClose = true;
    try {
      await this.data.run({ userName, password: this.password });
      this.password = ''; this.ref.close(true);
    } catch (error) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 401 &&
        ['authentication.invalid_credentials', 'authentication.unauthorized'].includes(error.error?.code)
        ? 'Credenciales de autorización inválidas. Revisa el usuario y la contraseña.' :
        error instanceof Error && !(error instanceof HttpErrorResponse) ? error.message : errorText(error));
      this.password = '';
    } finally { this.busy.set(false); this.ref.disableClose = false; }
  }
}

@Injectable({ providedIn: 'root' })
export class AuthorizationDialog {
  private readonly dialog = inject(MatDialog);
  private readonly session = inject(SessionService);
  private openDialog = false;
  /** True when the current user may run the operation without a second authorizer. */
  isSelfAuthorized(prompt: Pick<AuthorizationPrompt, 'selfAuthorizedRoles'>): boolean {
    return !!prompt.selfAuthorizedRoles?.length && this.session.hasRole(...prompt.selfAuthorizedRoles);
  }
  async open(prompt: AuthorizationPrompt): Promise<boolean> {
    if (this.isSelfAuthorized(prompt)) {
      if (this.openDialog) return false;
      this.openDialog = true;
      try { await prompt.run(null); return true; }
      catch (error) {
        if (!prompt.onError) throw error;
        prompt.onError(error instanceof Error && !(error instanceof HttpErrorResponse) ? error.message : errorText(error));
        return false;
      } finally { this.openDialog = false; }
    }
    if (this.openDialog) return false;
    this.openDialog = true;
    try {
      const ref = this.dialog.open(AuthorizationDialogComponent, {
        data: prompt, width: 'min(560px, calc(100vw - 32px))', maxWidth: '100vw', disableClose: true,
      });
      return await firstValueFrom(ref.afterClosed()) === true;
    } finally { this.openDialog = false; }
  }
}
