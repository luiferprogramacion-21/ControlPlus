import { Component, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { authorizationContext, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AuthorizationDialog, authorizationBody } from '../../shared/authorization-dialog';
import { dayDate } from '../../shared/day-date';
import { NumberInput } from '../../shared/number-input';
import { OperatorCard } from '../../shared/operator-card';
import { WindowSheet } from '../../shared/window-sheet';
import { CashMovement, CashState } from './cash-api';

interface ClosedShift { id: string; version: number; operatingDate: string; closedAtUtc: string; cashExpected: number; cashCounted: number; }
interface Review { id: string; originalCount: number; reviewedCount: number; reviewedDifference: number; reason: string; occurredAtUtc: string; }
type Kind = '' | 'INGRESO' | 'GASTO' | 'SANGRIA';

/** Nombre en palabras de tienda de un movimiento manual de caja. */
export function movementKindLabel(kind: string): string {
  return kind === 'INGRESO' || kind === 'INGRESO_MANUAL' ? 'Ingreso' : kind === 'GASTO' ? 'Gasto' : kind === 'SANGRIA' ? 'Retiro de efectivo' : 'Movimiento';
}

/**
 * Corregir un ingreso, gasto o retiro manual, o revisar el conteo de un cierre. Auditoría del 23 de noviembre de 2026
 * (A.16): se elige con tarjetas y se ve «Así quedó → Así quedará» antes de confirmar; las reglas y la API no cambian.
 */
@Component({ selector: 'cp-cash-corrections', imports: [FormsModule, NumberInput, WindowSheet, CurrencyPipe, DatePipe], styles: [`
  .cash-fix-choices { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; margin: 4px 0 14px; }
  .cash-fix-choice { display: grid; gap: 4px; padding: 14px; border: 1px solid var(--cp-border); border-radius: 12px; background: #fff; text-align: left; font: inherit; color: inherit; cursor: pointer; }
  .cash-fix-choice strong { font-size: 15px; } .cash-fix-choice small { color: var(--cp-text-muted); font-size: 13px; line-height: 1.4; }
  .cash-fix-choice--on { border: 2px solid var(--cp-primary, #2a5ea8); background: var(--cp-primary-soft, #eef4fc); }
  .cash-fix-summary { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); align-items: stretch; gap: 10px; margin: 12px 0; }
  .cash-fix-summary section { display: grid; gap: 4px; padding: 12px 14px; border: 1px solid var(--cp-border); border-radius: 10px; background: var(--cp-surface-subtle, #f7f9fc); }
  .cash-fix-summary section span { color: var(--cp-text-muted); font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .4px; }
  .cash-fix-summary section strong { font-size: 17px; } .cash-fix-summary section small { color: var(--cp-text-muted); }
  .cash-fix-summary > b { align-self: center; color: var(--cp-text-muted); font-size: 20px; }
  .cash-fix-effect { margin: 0 0 8px; font-weight: 700; }
  @container cp (max-width: 560px) { .cash-fix-summary { grid-template-columns: 1fr; } .cash-fix-summary > b { justify-self: center; transform: rotate(90deg); } }
`], template: `
  @if (canCorrect) { <button class="button button--secondary" type="button" (click)="show()">Corregir un error de caja</button> }
  @if (opened()) { <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="cash-correction-title">
    <div class="module-form-sheet__backdrop" (click)="!busy() && opened.set(false)"></div>
    <form class="panel panel--padded module-form module-form--wide" (submit)="$event.preventDefault(); confirm()">
      <h2 id="cash-correction-title">Corregir un error de caja</h2>
      <p class="muted">Nada se borra: lo original queda en el historial y la corrección queda unida a él.</p>
      <div class="cash-fix-choices" role="radiogroup" aria-label="Qué necesitas corregir">
        @if (canMovements) {
          <button type="button" role="radio" class="cash-fix-choice" [class.cash-fix-choice--on]="mode === 'movement'" [attr.aria-checked]="mode === 'movement'" (click)="setMode('movement')" [disabled]="busy()">
            <strong>Un ingreso, gasto o retiro quedó mal</strong><small>Se anula el movimiento equivocado y, si quieres, se registra el correcto en la caja de hoy.</small></button>
        }
        @if (isAdmin) {
          <button type="button" role="radio" class="cash-fix-choice" [class.cash-fix-choice--on]="mode === 'review'" [attr.aria-checked]="mode === 'review'" (click)="setMode('review')" [disabled]="busy()">
            <strong>El conteo de un cierre quedó mal</strong><small>Anota cuánto había de verdad en un día ya cerrado. No mueve dinero de la caja de hoy.</small></button>
        }
      </div>
      @if (mode === 'movement') {
        @if (!state()?.currentShift) { <p class="notice notice--warning">Abre la caja de hoy: la corrección se registra en el turno abierto.</p> }
        <label class="field-label">¿Cuál movimiento quedó mal?<select class="text-field" name="movement" [ngModel]="movementId" (ngModelChange)="selectMovement($event)" [disabled]="busy()"><option value="">Elige el movimiento</option>
          @for (item of manualMovements; track item.id) { <option [value]="item.id">{{ kindLabel(item.category) }} · {{ item.amount | currency:'COP':'symbol':'1.0-0' }} · {{ item.concept || 'Sin concepto' }} · {{ item.occurredAtUtc | date:'shortTime' }}</option> }
        </select></label>
        @if (!manualMovements.length) { <p class="muted">Hoy no hay ingresos, gastos ni retiros manuales para corregir.</p> }
        @if (selectedMovement; as item) {
          <label class="field-label field-label--spaced">¿Qué debe quedar?<select class="text-field" name="kind" [(ngModel)]="kind" [disabled]="busy()">
            <option value="">Nada: no debía registrarse</option><option value="INGRESO">Un ingreso</option><option value="GASTO">Un gasto</option><option value="SANGRIA">Un retiro de efectivo</option>
          </select></label>
          @if (kind) {
            <label class="field-label">Monto correcto<input class="text-field" cpNumberInput min="1" step="1" name="amount" [(ngModel)]="amount" [disabled]="busy()"></label>
            <label class="field-label">Concepto correcto<input class="text-field" name="concept" maxlength="500" [(ngModel)]="concept" [disabled]="busy()"></label>
          }
          <div class="cash-fix-summary" aria-label="Antes y después">
            <section><span>Así quedó</span><strong>{{ kindLabel(item.category) }} {{ item.amount | currency:'COP':'symbol':'1.0-0' }}</strong><small>{{ item.concept || 'Sin concepto' }}</small></section>
            <b aria-hidden="true">→</b>
            <section><span>Así quedará</span>
              @if (kind) { <strong>{{ kindLabel(kind) }} {{ amount | currency:'COP':'symbol':'1.0-0' }}</strong><small>{{ concept || 'Sin concepto' }}</small> }
              @else { <strong>Anulado</strong><small>Como si no se hubiera registrado.</small> }</section>
          </div>
          <p class="cash-fix-effect" role="status">{{ cashEffect }}</p>
        }
        <p class="form-hint">Si el dinero con que se abrió la caja quedó mal, registra la diferencia como ingreso o gasto con el concepto «Corrección del fondo inicial».</p>
      } @else {
        <label class="field-label">¿Qué cierre quedó mal contado?<select class="text-field" name="shift" [(ngModel)]="shiftId" (ngModelChange)="loadReviews()" [disabled]="busy()"><option value="">Elige el día</option>
          @for (shift of closedShifts(); track shift.id) { <option [value]="shift.id">{{ day(shift.operatingDate) }} · cerrado a las {{ shift.closedAtUtc | date:'shortTime' }}</option> }
        </select></label>
        @if (selectedShift; as shift) {
          <label class="field-label field-label--spaced">¿Cuánto efectivo había de verdad?<input class="text-field" cpNumberInput min="0" step="1" name="reviewedCount" [(ngModel)]="reviewedCount" [disabled]="busy()"></label>
          <div class="cash-fix-summary" aria-label="Antes y después">
            <section><span>Así quedó</span><strong>Se contaron {{ shift.cashCounted | currency:'COP':'symbol':'1.0-0' }}</strong><small>Debía haber {{ shift.cashExpected | currency:'COP':'symbol':'1.0-0' }} · diferencia {{ (shift.cashCounted - shift.cashExpected) | currency:'COP':'symbol':'1.0-0' }}</small></section>
            <b aria-hidden="true">→</b>
            <section><span>Así quedará</span><strong>Había {{ reviewedCount | currency:'COP':'symbol':'1.0-0' }}</strong><small>Diferencia corregida {{ (reviewedCount - shift.cashExpected) | currency:'COP':'symbol':'1.0-0' }}</small></section>
          </div>
          <p class="form-hint">El cierre original no cambia; esta revisión queda guardada junto a él.</p>
        }
        @if (reviews().length) { <p class="field-label field-label--spaced">Revisiones anteriores de este cierre</p> }
        @for (review of reviews(); track review.id) { <p class="muted">{{ review.occurredAtUtc | date:'short' }} · Había {{ review.reviewedCount | currency:'COP':'symbol':'1.0-0' }} · {{ review.reason }}</p> }
      }
      <label class="field-label field-label--spaced">¿Por qué se corrige? <span>(obligatorio)</span><textarea class="text-field textarea" name="reason" maxlength="500" rows="2" [(ngModel)]="reason" [disabled]="busy()" placeholder="Ejemplo: el gasto era de 15.000, no de 20.000"></textarea></label>
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
  private readonly operatorCard = inject(OperatorCard);
  mode: 'movement' | 'review' = 'movement'; movementId = ''; kind: Kind = ''; amount = 0; concept = ''; reason = ''; shiftId = ''; reviewedCount = 0;
  private pending: { fingerprint: string; key: string } | null = null;
  readonly kindLabel = movementKindLabel;
  get isAdmin(): boolean { return this.session.hasRole('ADMINISTRADOR'); }
  get canMovements(): boolean { return this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR') && this.session.has('CASH.MOVEMENTS_MANAGE'); }
  get canCorrect(): boolean { return this.canMovements || this.isAdmin && this.session.has('CASH.SHIFT_MANAGE'); }
  get manualMovements(): CashMovement[] { return this.movements().filter(x => (x.type === 'INGRESO' && x.category === 'INGRESO_MANUAL') || (x.type === 'EGRESO' && ['GASTO', 'SANGRIA'].includes(x.category))); }
  get selectedMovement(): CashMovement | undefined { return this.manualMovements.find(x => x.id === this.movementId); }
  get selectedShift(): ClosedShift | undefined { return this.closedShifts().find(x => x.id === this.shiftId); }
  get cashDelta(): number { const original = this.selectedMovement; return (original ? (original.type === 'INGRESO' ? -original.amount : original.amount) : 0) + (this.kind ? (this.kind === 'INGRESO' ? this.amount : -this.amount) : 0); }
  /** Qué le pasa al efectivo de la caja abierta, en una frase. */
  get cashEffect(): string {
    const delta = this.cashDelta, money = (value: number) => '$' + Math.abs(value).toLocaleString('es-CO');
    return delta === 0 ? 'El efectivo de la caja de hoy no cambia.' : delta > 0 ? `El efectivo de la caja de hoy sube ${money(delta)}.` : `El efectivo de la caja de hoy baja ${money(delta)}.`;
  }
  day(value: string): string { return dayDate(value); }
  show(): void {
    this.error.set(''); this.notice.set(''); this.opened.set(true);
    if (!this.canMovements && this.isAdmin) this.setMode('review');
  }
  setMode(mode: 'movement' | 'review'): void { if (this.busy()) return; this.mode = mode; void this.modeChanged(); }
  /** Al elegir el movimiento se propone el mismo tipo, monto y concepto: lo usual es corregir solo el monto. */
  selectMovement(id: string): void {
    this.movementId = id; const item = this.selectedMovement;
    if (!item) { this.kind = ''; this.amount = 0; this.concept = ''; return; }
    this.kind = item.category === 'INGRESO_MANUAL' ? 'INGRESO' : item.category as Kind; this.amount = item.amount; this.concept = item.concept ?? '';
  }
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
    if (this.mode === 'movement' && !shift) { this.error.set('Abre la caja de hoy para registrar la corrección.'); return; }
    if (this.mode === 'movement' && !this.selectedMovement) { this.error.set('Elige el movimiento que quedó mal.'); return; }
    if (this.mode === 'movement' && this.kind && (!Number.isSafeInteger(this.amount) || this.amount <= 0 || !this.concept.trim())) { this.error.set('Escribe el monto correcto (mayor que cero) y su concepto.'); return; }
    if (this.mode === 'movement' && this.kind && this.cashDelta === 0 && this.concept.trim() === (this.selectedMovement!.concept ?? '').trim() &&
      this.kind === (this.selectedMovement!.category === 'INGRESO_MANUAL' ? 'INGRESO' : this.selectedMovement!.category)) { this.error.set('No cambiaste nada: ajusta el monto, el tipo o el concepto, o elige «Nada».'); return; }
    if (this.mode === 'review' && (!this.selectedShift || !Number.isSafeInteger(this.reviewedCount) || this.reviewedCount < 0)) { this.error.set('Elige el día y escribe cuánto efectivo había, en pesos enteros.'); return; }
    const review = this.mode === 'review';
    // 25/11/2026: en turno compartido la corrección se hace con la sesión del carné; si nadie lo ha pasado, se pide antes.
    let sessionId: string | null = null;
    if (!review) {
      const operator = await this.operatorCard.ensure(this.cashEffect);
      if (!operator) return;
      sessionId = operator.sessionId;
    }
    await this.authorization.open({ title: review ? 'Registrar revisión del cierre' : 'Autorizar corrección de caja',
      description: review ? 'Se añade la revisión al historial sin cambiar el cierre ni el dinero de la caja de hoy.' : 'Se anula el movimiento equivocado y se registra el correcto, sin borrar el original.',
      detail: review ? `Había: $${this.reviewedCount.toLocaleString('es-CO')}` : this.cashEffect,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      selfAuthorizedRoles: ['ADMINISTRADOR'], onError: e => this.error.set(e), run: async credentials => {
        const body = review ? { expectedShiftVersion: this.selectedShift!.version, reviewedCount: this.reviewedCount, reason: this.reason.trim() }
          : { expectedShiftVersion: shift!.version, reason: this.reason.trim(), replacementKind: this.kind || null,
              replacementAmount: this.kind ? this.amount : null, replacementConcept: this.kind ? this.concept.trim() : null,
              operatorSessionId: sessionId, ...(credentials ? { authorization: authorizationBody(credentials) } : {}) };
        const url = review ? `/api/cash/shifts/${this.shiftId}/reviews` : `/api/cash/movements/${this.movementId}/correction`;
        const fingerprint = JSON.stringify({ url, ...body, authorization: undefined });
        if (this.pending?.fingerprint !== fingerprint) this.pending = { fingerprint, key: crypto.randomUUID() };
        this.busy.set(true); this.error.set('');
        try { await firstValueFrom(this.http.post(url, body, { headers: new HttpHeaders({ 'Idempotency-Key': this.pending.key }), context: credentials ? authorizationContext() : undefined }));
          this.pending = null; this.notice.set(review ? 'Revisión guardada. El cierre original se conserva.' : 'Corrección registrada. Actualizamos la caja.');
          if (review) await this.loadReviews(); else { this.opened.set(false); this.corrected.emit(); }
        } catch (e) { this.error.set(errorText(e)); throw e; } finally { this.busy.set(false); }
      } });
  }
}
