import { Component, inject, Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { errorText } from '../core/api';
import { toastSignal } from '../core/toast';
import { CashApi, CashState } from '../features/cash/cash-api';

/** Quién hace la operación (pruebas del usuario del 25/11/2026). */
export interface OperatorContext {
  /** Turno compartido: la operación queda a nombre de quien pasó su carné y se valida con su rol. */
  shared: boolean;
  /** Rol de quien pasó su carné (solo en turno compartido). */
  role: string | null;
  /** Sesión de operador activa en la terminal (solo en turno compartido). */
  sessionId: string | null;
}

@Component({
  selector: 'cp-operator-card-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule],
  template: `
    <div class="operator-card-dialog">
      <h2 mat-dialog-title class="window-heading">Pasa tu carné</h2><button class="window-close window-close--dialog" type="button" aria-label="Cerrar" title="Cerrar (Esc)" (click)="cancel()" [disabled]="busy()"><svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12 M6 18L18 6"/></svg></button>
      <mat-dialog-content>
        <p>El turno es compartido: la operación queda a nombre de quien pasa su carné.</p>
        @if (data.detail) { <p><strong>{{ data.detail }}</strong></p> }
        <form id="operator-card-form" (ngSubmit)="submit()">
          <label for="operator-card-scan">Carné de quien hace la operación</label>
          <input id="operator-card-scan" class="text-field" type="password" name="scan" [(ngModel)]="token" autocomplete="off" cdkFocusInitial
            placeholder="Pase el carné por el lector" [disabled]="busy()">
        </form>
        @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
        @if (busy()) { <p role="status">Revisando el carné…</p> }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-flat-button type="submit" form="operator-card-form" [disabled]="busy()">{{ busy() ? 'Revisando…' : 'Continuar' }}</button>
        <button mat-button type="button" (click)="cancel()" [disabled]="busy()">Cancelar</button>
      </mat-dialog-actions>
    </div>`,
  styles: [`
    .operator-card-dialog { min-width:min(420px, calc(calc(100vw / var(--cp-zoom)) - 48px)); max-width:560px; }
    mat-dialog-content { display:grid; gap:10px; }
    mat-dialog-content p { margin:0; line-height:1.5; }
    label { font-weight:600; margin-top:6px; }
    .text-field { width:100%; box-sizing:border-box; }
    .field-error { color:var(--cp-error); }
  `],
})
export class OperatorCardDialogComponent {
  readonly data = inject<{ detail: string }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<OperatorCardDialogComponent>);
  private readonly cash = inject(CashApi);
  readonly busy = signal(false);
  readonly error = toastSignal('error');
  token = '';

  constructor() { this.ref.keydownEvents().subscribe(event => { if (event.key === 'Escape') this.cancel(); }); }

  cancel(): void { if (this.busy()) return; this.token = ''; this.ref.close(false); }
  /** El lector escribe el código y pulsa Enter: se inicia la sesión de quien pasó el carné. */
  async submit(): Promise<void> {
    const token = this.token.trim();
    if (this.busy()) return;
    if (!token) { this.error.set('Pasa tu carné por el lector.'); return; }
    this.busy.set(true); this.error.set(''); this.ref.disableClose = true;
    try {
      await this.cash.startSession(token);
      this.token = ''; this.ref.close(true);
    } catch (error) {
      this.token = '';
      this.error.set(error instanceof HttpErrorResponse && error.status === 401
        ? 'El carné no es válido, fue reemplazado o venció. Pásalo de nuevo o pide uno nuevo al Administrador.' : errorText(error));
    } finally { this.busy.set(false); this.ref.disableClose = false; }
  }
}

/**
 * Pruebas del usuario del 25/11/2026: en turno compartido toda operación queda a nombre de quien pasa su carné. Si nadie lo
 * ha pasado, en lugar de bloquear el botón o fallar al final, se pide el carné antes de seguir.
 */
@Injectable({ providedIn: 'root' })
export class OperatorCard {
  private readonly dialog = inject(MatDialog);
  private readonly cash = inject(CashApi);
  private asking = false;

  /** Devuelve quién opera, o null si se canceló. Si no se puede consultar la caja, sigue y decide el servidor. */
  async ensure(detail = ''): Promise<OperatorContext | null> {
    let state: CashState;
    try { state = await this.cash.state(); }
    catch { return { shared: false, role: null, sessionId: null }; }
    if (!state.hasOpenShift || !state.requiresOperatorSession) return { shared: false, role: null, sessionId: null };
    if (!state.ownOperatorSession) {
      if (this.asking) return null;
      this.asking = true;
      try {
        const ref = this.dialog.open(OperatorCardDialogComponent, {
          data: { detail }, width: 'min(520px, calc(calc(100vw / var(--cp-zoom)) - 32px))', maxWidth: 'calc(100vw / var(--cp-zoom))', disableClose: true,
        });
        if (await firstValueFrom(ref.afterClosed()) !== true) return null;
      } finally { this.asking = false; }
      try { state = await this.cash.state(); }
      catch { return { shared: true, role: null, sessionId: null }; }
    }
    return { shared: true, role: state.activeOperatorRole ?? null, sessionId: state.ownOperatorSession?.id ?? null };
  }
}
