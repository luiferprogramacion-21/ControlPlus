import { Component, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { authorizationContext, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, authorizationBody } from '../../shared/authorization-dialog';
import { NumberInput } from '../../shared/number-input';
import { WindowSheet } from '../../shared/window-sheet';
import { CashMovement, CashState } from './cash-api';

interface ClosedShift { id: string; version: number; operatingDate: string; closedAtUtc: string; cashExpected: number; cashCounted: number; }
interface Review { id: string; originalCount: number; reviewedCount: number; reviewedDifference: number; reason: string; occurredAtUtc: string; }

@Component({ selector: 'cp-cash-corrections', imports: [FormsModule, NumberInput, WindowSheet, CurrencyPipe, DatePipe], template: `
  @if (canCorrect) { <button class="button button--secondary" type="button" (click)="show()">Corregir un error de caja</button> }
  @if (opened()) { <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="cash-correction-title">
    <div class="module-form-sheet__backdrop" (click)="!busy() && opened.set(false)"></div>
    <form class="panel panel--padded module-form" (submit)="$event.preventDefault(); confirm()">
      <h2 id="cash-correction-title">Corregir un error de caja</h2>
      <label class="field-label">Qué necesitas corregir<select class="text-field" name="mode" [(ngModel)]="mode" (ngModelChange)="modeChanged()" [disabled]="busy()">
        <option value="movement">Ingreso, gasto o retiro manual</option>@if (isAdmin) { <option value="review">Revisar el conteo de un cierre</option> }
      </select></label>
      @if (mode === 'movement') {
        @if (!state()?.currentShift) { <p class="notice notice--warning">Abre tu turno para registrar el reverso y el movimiento correcto.</p> }
        <p>El movimiento original queda en el historial. Se registra su reverso y, si corresponde, el movimiento correcto en tu turno abierto.</p>
        <label class="field-label">Movimiento<select class="text-field" name="movement" [(ngModel)]="movementId" [disabled]="busy()"><option value="">Selecciona un movimiento</option>
          @for (item of manualMovements; track item.id) { <option [value]="item.id">{{ item.concept || item.category }} · {{ item.amount | currency:'COP':'symbol':'1.0-0' }} · {{ item.occurredAtUtc | date:'short' }}</option> }
        </select></label>
        @if (!manualMovements.length) { <p>No hay movimientos manuales recientes para corregir.</p> }
        <label class="field-label">Qué debe quedar<select class="text-field" name="kind" [(ngModel)]="kind" [disabled]="busy()">
          <option value="">Anularlo, sin reemplazo</option><option value="INGRESO">Ingreso correcto</option><option value="GASTO">Gasto correcto</option><option value="SANGRIA">Retiro correcto</option>
        </select></label>
        @if (kind) {
          <label class="field-label">Monto correcto<input class="text-field" cpNumberInput min="1" step="1" name="amount" [(ngModel)]="amount" [disabled]="busy()"></label>
          <label class="field-label">Concepto correcto<input class="text-field" name="concept" maxlength="500" [(ngModel)]="concept" [disabled]="busy()"></label>
        }
        @if (selectedMovement; as item) { <div class="notice"><span>Se revierte {{ item.amount | currency:'COP':'symbol':'1.0-0' }}.</span><strong>Cambio neto del efectivo: {{ cashDelta | currency:'COP':'symbol':'1.0-0' }}</strong></div> }
        <p class="form-hint">Si el fondo inicial quedó mal, registra la diferencia como ingreso o gasto manual con concepto «Corrección del fondo inicial». La apertura original se conserva.</p>
      } @else {
        <p>La revisión deja constancia del conteo corregido. Conserva el cierre original y no mueve dinero al turno actual.</p>
        <label class="field-label">Cierre<select class="text-field" name="shift" [(ngModel)]="shiftId" (ngModelChange)="loadReviews()" [disabled]="busy()"><option value="">Selecciona un cierre</option>
          @for (shift of closedShifts(); track shift.id) { <option [value]="shift.id">{{ shift.operatingDate }} · {{ shift.closedAtUtc | date:'short' }}</option> }
        </select></label>
        @if (selectedShift; as shift) {
          <p>Esperado {{ shift.cashExpected | currency:'COP':'symbol':'1.0-0' }} · Conteo original {{ shift.cashCounted | currency:'COP':'symbol':'1.0-0' }}</p>
          <label class="field-label">Conteo revisado<input class="text-field" cpNumberInput min="0" step="1" name="reviewedCount" [(ngModel)]="reviewedCount" [disabled]="busy()"></label>
          <p>Diferencia revisada: <strong>{{ (reviewedCount - shift.cashExpected) | currency:'COP':'symbol':'1.0-0' }}</strong></p>
        }
        @for (review of reviews(); track review.id) { <p>{{ review.occurredAtUtc | date:'short' }} · Revisado {{ review.reviewedCount | currency:'COP':'symbol':'1.0-0' }} · {{ review.reason }}</p> }
      }
      <label class="field-label field-label--spaced">Motivo de la corrección<textarea class="text-field textarea" name="reason" maxlength="500" rows="2" [(ngModel)]="reason" [disabled]="busy()"></textarea></label>
      @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
      @if (notice()) { <p class="notice" role="status">{{ notice() }}</p> }
      <div class="form-actions"><button class="button button--primary" type="submit" [disabled]="busy() || !reason.trim()">{{ busy() ? 'Registrando…' : 'Revisar y confirmar' }}</button><button class="button button--secondary" type="button" (click)="opened.set(false)" [disabled]="busy()">Cerrar</button></div>
    </form>
  </div> }`,
})
export class CashCorrections {
  readonly state = input<CashState | null>(null);
  readonly movements = input<CashMovement[]>([]);
  readonly corrected = output<void>();
  readonly opened = signal(false); readonly busy = signal(false); readonly error = signal(''); readonly notice = signal('');
  readonly closedShifts = signal<ClosedShift[]>([]); readonly reviews = signal<Review[]>([]);
  private readonly http = inject(HttpClient); private readonly session = inject(SessionService); private readonly authorization = inject(AuthorizationDialog);
  mode: 'movement' | 'review' = 'movement'; movementId = ''; kind = ''; amount = 0; concept = ''; reason = ''; shiftId = ''; reviewedCount = 0;
  private pending: { fingerprint: string; key: string } | null = null;
  get isAdmin(): boolean { return this.session.hasRole('ADMINISTRADOR'); }
  get canCorrect(): boolean { return this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR') && this.session.has('CASH.MOVEMENTS_MANAGE') || this.isAdmin && this.session.has('CASH.SHIFT_MANAGE'); }
  get manualMovements(): CashMovement[] { return this.movements().filter(x => (x.type === 'INGRESO' && x.category === 'INGRESO_MANUAL') || (x.type === 'EGRESO' && ['GASTO', 'SANGRIA'].includes(x.category))); }
  get selectedMovement(): CashMovement | undefined { return this.manualMovements.find(x => x.id === this.movementId); }
  get selectedShift(): ClosedShift | undefined { return this.closedShifts().find(x => x.id === this.shiftId); }
  get cashDelta(): number { const original = this.selectedMovement; return (original ? (original.type === 'INGRESO' ? -original.amount : original.amount) : 0) + (this.kind ? (this.kind === 'INGRESO' ? this.amount : -this.amount) : 0); }
  show(): void { this.error.set(''); this.notice.set(''); this.opened.set(true); if (!this.session.has('CASH.MOVEMENTS_MANAGE') && this.isAdmin) { this.mode = 'review'; void this.modeChanged(); } }
  async modeChanged(): Promise<void> {
    this.error.set(''); this.notice.set('');
    if (this.mode !== 'review') return;
    try { this.closedShifts.set(await firstValueFrom(this.http.get<ClosedShift[]>('/api/cash/closed-shifts'))); }
    catch (e) { this.error.set(errorText(e)); }
  }
  async loadReviews(): Promise<void> {
    this.reviews.set([]); if (!this.shiftId) return;
    this.reviewedCount = this.selectedShift?.cashCounted ?? 0;
    try { this.reviews.set(await firstValueFrom(this.http.get<Review[]>(`/api/cash/shifts/${this.shiftId}/reviews`))); }
    catch (e) { this.error.set(errorText(e)); }
  }
  async confirm(): Promise<void> {
    if (this.busy() || !this.reason.trim()) return;
    const shift = this.state()?.currentShift;
    if (this.mode === 'movement' && (!shift || !this.selectedMovement || (this.kind && (!Number.isSafeInteger(this.amount) || this.amount <= 0 || !this.concept.trim())))) { this.error.set('Selecciona un movimiento, abre tu turno y revisa monto y concepto.'); return; }
    if (this.mode === 'review' && (!this.selectedShift || !Number.isSafeInteger(this.reviewedCount) || this.reviewedCount < 0)) { this.error.set('Selecciona un cierre e indica un conteo entero no negativo.'); return; }
    const review = this.mode === 'review';
    await this.authorization.open({ title: review ? 'Registrar revisión del cierre' : 'Autorizar corrección de caja',
      description: review ? 'Se añade la revisión al historial sin cambiar el cierre ni el dinero del turno actual.' : 'Se registra un reverso vinculado y el movimiento correcto, sin borrar el original.',
      detail: review ? `Conteo revisado: ${this.reviewedCount.toLocaleString('es-CO')}` : `Cambio neto del efectivo: ${this.cashDelta.toLocaleString('es-CO')}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      selfAuthorizedRoles: ['ADMINISTRADOR'], onError: e => this.error.set(e), run: async credentials => {
        const body = review ? { expectedShiftVersion: this.selectedShift!.version, reviewedCount: this.reviewedCount, reason: this.reason.trim() }
          : { expectedShiftVersion: shift!.version, reason: this.reason.trim(), replacementKind: this.kind || null,
              replacementAmount: this.kind ? this.amount : null, replacementConcept: this.kind ? this.concept.trim() : null,
              operatorSessionId: this.state()?.ownOperatorSession?.id ?? null, ...(credentials ? { authorization: authorizationBody(credentials) } : {}) };
        const url = review ? `/api/cash/shifts/${this.shiftId}/reviews` : `/api/cash/movements/${this.movementId}/correction`;
        const fingerprint = JSON.stringify({ url, ...body, authorization: undefined });
        if (this.pending?.fingerprint !== fingerprint) this.pending = { fingerprint, key: crypto.randomUUID() };
        this.busy.set(true); this.error.set('');
        try { await firstValueFrom(this.http.post(url, body, { headers: new HttpHeaders({ 'Idempotency-Key': this.pending.key }), context: credentials ? authorizationContext() : undefined }));
          this.pending = null; this.notice.set(review ? 'Revisión registrada. El cierre original se conserva.' : 'Corrección registrada. Actualizamos la caja.');
          if (review) await this.loadReviews(); else { this.opened.set(false); this.corrected.emit(); }
        } catch (e) { this.error.set(errorText(e)); throw e; } finally { this.busy.set(false); }
      } });
  }
}
