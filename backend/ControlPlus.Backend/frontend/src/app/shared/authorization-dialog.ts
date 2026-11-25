import { Component, inject, Injectable, Injector, OnDestroy, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, errorText } from '../core/api';
import { SessionService } from '../core/session.service';
import { toastSignal } from '../core/toast';
import { OperatorCard, OperatorContext } from './operator-card';

/** User name and password, or (userName/password empty) the Code 128 credential scanned from the authorizer's card. */
export interface AuthorizationCredentials { userName: string; password: string; credentialToken?: string; }
/** Request body of an authorization: the scanned credential replaces the user name and password. */
export function authorizationBody(credentials: AuthorizationCredentials): AuthorizationBody {
  return credentials.credentialToken ? { credentialToken: credentials.credentialToken } : { userName: credentials.userName, password: credentials.password };
}
/** Stable, non-secret identity of the authorizer (for idempotent retries). */
export function authorizationIdentity(credentials: AuthorizationCredentials | null): string | null {
  if (!credentials) return null;
  return credentials.credentialToken ? 'CRED:' + credentials.credentialToken.slice(-8) : credentials.userName.toUpperCase();
}
export interface AuthorizationPrompt {
  title: string;
  description: string;
  detail: string;
  operatorUserName: string;
  requireDifferentUser: boolean;
  /** Offers a field to scan the authorizer's Code 128 card instead of typing user and password. */
  allowCredential?: boolean;
  /** Roles that run the operation without a second authorizer (run receives null). Business limits and audit still apply on the server. */
  selfAuthorizedRoles?: string[];
  /**
   * La operación queda a nombre de quien opera (turno compartido: el dueño del carné). Si nadie ha pasado su carné, se pide
   * antes de la autorización, y los roles que se autorizan solos se miden con el rol de ese dueño (25/11/2026).
   */
  operatorScoped?: boolean;
  /** Shows errors of the dialog-less path; without it they are rethrown. */
  onError?: (message: string) => void;
  run: (credentials: AuthorizationCredentials | null) => Promise<void>;
}

@Component({
  selector: 'cp-authorization-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule],
  template: `
    <div class="authorization-dialog">
      <h2 mat-dialog-title class="window-heading">{{ data.title }}</h2><button class="window-close window-close--dialog" type="button" aria-label="Cerrar" title="Cerrar (Esc)" (click)="cancel()" [disabled]="busy()"><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12 M6 18L18 6"/></svg></button>
      <mat-dialog-content>
        <p>{{ data.description }}</p><p><strong>{{ data.detail }}</strong></p>
        @if (data.allowCredential) {
          <label for="authorization-scan">Pasar el carné de quien autoriza</label>
          <input id="authorization-scan" class="text-field" type="password" name="scan" [(ngModel)]="scanToken" autocomplete="off" cdkFocusInitial
            placeholder="Pase el carné por el lector" (keydown.enter)="scan($event)" [disabled]="busy()">
          <p class="scan-hint">o escribe el usuario y la contraseña:</p>
        }
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
        <button mat-flat-button type="submit" form="authorization-form" [disabled]="busy()">{{ busy() ? 'Procesando…' : 'Autorizar y continuar' }}</button>
        <button mat-button type="button" (click)="cancel()" [disabled]="busy()">Cancelar</button>
      </mat-dialog-actions>
    </div>`,
  styles: [`
    .authorization-dialog { min-width:min(420px, calc(calc(100vw / var(--cp-zoom)) - 48px)); max-width:560px; }
    mat-dialog-content { display:grid; gap:10px; }
    mat-dialog-content p { margin:0; line-height:1.5; }
    label { font-weight:600; margin-top:6px; }
    .text-field { width:100%; box-sizing:border-box; }
    .field-error { color:var(--cp-error); }
    .scan-hint { color:var(--cp-text-muted, inherit); font-size:.9em; }
  `],
})
export class AuthorizationDialogComponent implements OnDestroy {
  readonly data = inject<AuthorizationPrompt>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<AuthorizationDialogComponent>);
  readonly busy = signal(false);
  readonly error = toastSignal('error');
  userName = '';
  password = '';
  scanToken = '';

  ngOnDestroy(): void { this.password = ''; this.scanToken = ''; }

  cancel(): void { if (this.busy()) return; this.password = ''; this.scanToken = ''; this.ref.close(false); }
  /** The reader types the token and presses Enter: authorize right away. */
  async scan(event?: Event): Promise<void> {
    event?.preventDefault();
    const credentialToken = this.scanToken.trim();
    if (!credentialToken || this.busy()) return;
    await this.execute({ userName: '', password: '', credentialToken });
  }
  async submit(): Promise<void> {
    if (this.busy()) return;
    const userName = this.userName.trim();
    if (!userName || !this.password) { this.error.set('Ingresa usuario y contraseña del autorizador.'); return; }
    if (this.data.requireDifferentUser && userName.toLocaleLowerCase() === this.data.operatorUserName.trim().toLocaleLowerCase()) {
      this.error.set('El autorizador debe ser otro usuario.'); this.password = ''; return;
    }
    await this.execute({ userName, password: this.password });
  }
  private async execute(credentials: AuthorizationCredentials): Promise<void> {
    this.busy.set(true); this.error.set(''); this.ref.disableClose = true;
    try {
      await this.data.run(credentials);
      this.password = ''; this.scanToken = ''; this.ref.close(true);
    } catch (error) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 401 &&
        ['authentication.invalid_credentials', 'authentication.unauthorized'].includes(error.error?.code)
        ? 'Credenciales de autorización inválidas. Revisa el usuario y la contraseña, o vuelve a pasar el carné.' :
        error instanceof Error && !(error instanceof HttpErrorResponse) ? error.message : errorText(error));
      this.password = ''; this.scanToken = '';
    } finally { this.busy.set(false); this.ref.disableClose = false; }
  }
}

@Injectable({ providedIn: 'root' })
export class AuthorizationDialog {
  private readonly dialog = inject(MatDialog);
  private readonly session = inject(SessionService);
  /** Se obtiene al usarla: la mayoría de las autorizaciones no son de turno. */
  private readonly injector = inject(Injector);
  private openDialog = false;
  /** True when the current user may run the operation without a second authorizer. */
  isSelfAuthorized(prompt: Pick<AuthorizationPrompt, 'selfAuthorizedRoles'>): boolean {
    return !!prompt.selfAuthorizedRoles?.length && this.session.hasRole(...prompt.selfAuthorizedRoles);
  }
  async open(prompt: AuthorizationPrompt): Promise<boolean> {
    if (this.openDialog) return false;
    let selfAuthorized = this.isSelfAuthorized(prompt);
    if (prompt.operatorScoped) {
      this.openDialog = true;
      let operator: OperatorContext | null;
      try { operator = await this.injector.get(OperatorCard).ensure(prompt.detail); } finally { this.openDialog = false; }
      if (!operator) return false;
      if (operator.shared) {
        selfAuthorized = !!operator.role && !!prompt.selfAuthorizedRoles?.includes(operator.role);
        prompt = { ...prompt, operatorUserName: '', requireDifferentUser: false };
      }
    }
    if (selfAuthorized) {
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
        data: prompt, width: 'min(560px, calc(calc(100vw / var(--cp-zoom)) - 32px))', maxWidth: 'calc(100vw / var(--cp-zoom))', disableClose: true,
      });
      return await firstValueFrom(ref.afterClosed()) === true;
    } finally { this.openDialog = false; }
  }
}
