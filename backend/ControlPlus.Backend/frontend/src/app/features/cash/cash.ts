import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { CashApi, CashMovement, CashState, ClosureDifferenceReason, Reconciliation } from './cash-api';
import { AuthorizationCredentials, AuthorizationDialog } from '../../shared/authorization-dialog';

@Component({
  selector: 'cp-cash',
  imports: [FormsModule, CurrencyPipe, DatePipe, MatButtonModule],
  templateUrl: './cash.html',
  styleUrl: './cash.scss',
})
export class Cash implements OnInit {
  readonly api = inject(CashApi);
  private readonly authorization = inject(AuthorizationDialog);
  readonly session = inject(SessionService);
  readonly state = signal<CashState | null>(null);
  readonly reconciliation = signal<Reconciliation | null>(null);
  readonly closingMode = signal(false);
  readonly movements = signal<CashMovement[]>([]);
  readonly loading = signal(false);
  readonly stateLoading = signal(true);
  readonly stateReadError = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly reasons = signal<ClosureDifferenceReason[] | null>(null);
  readonly reasonsLoading = signal(false);
  readonly reasonsError = signal('');
  initialAmount = 0;
  openObservations = '';
  kind: 'incomes' | 'expenses' | 'cash-drops' = 'incomes';
  amount = 0;
  concept = '';
  credential = '';
  counted: Record<string, number | null> = {};
  reasonId = '';
  differenceObservation = '';
  ngOnInit(): void { void this.refresh(); }
  modeLabel(mode: string | null | undefined): string {
    return mode === 'INDIVIDUAL' ? 'Individual' : mode === 'COMPARTIDO' ? 'Compartido' : 'No disponible';
  }
  configuredModeLabel(mode: string | null | undefined = this.state()?.defaultShiftMode): string {
    if (this.stateLoading()) return 'Cargando…';
    if (this.stateReadError()) return 'No disponible';
    return this.modeLabel(mode);
  }
  get difference(): number { return this.hasValidCounts ? this.methods().reduce((sum, method) => sum + (Number(this.counted[method.paymentMethodId]) - method.expectedAmount), 0) : 0; }
  get hasValidCounts(): boolean { return this.methods().every(method => {
    const value = this.counted[method.paymentMethodId];
    return typeof value === 'number' && Number.isSafeInteger(value) && (!method.affectsCash || value >= 0);
  }); }
  methods() { return this.reconciliation()?.paymentMethods ?? []; }
  methodDifference(id: string, expected: number): number { return typeof this.counted[id] === 'number' ? Number(this.counted[id]) - expected : 0; }
  validMoney(value: number, allowZero = false): boolean { return Number.isSafeInteger(value) && (allowZero ? value >= 0 : value > 0); }
  async refresh(): Promise<void> {
    this.loading.set(true); this.stateLoading.set(true); this.error.set('');
    let stateLoaded = false;
    try {
      const state = await this.api.state(); stateLoaded = true; this.stateLoading.set(false); this.stateReadError.set(false); this.state.set(state);
      if (state.hasOpenShift) {
        this.movements.set((await this.api.movements()).items);
        if (this.session.has('CASH.SHIFT_MANAGE') && state.currentShift) {
          if (this.reconciliation()?.shiftId !== state.currentShift.id) { this.reconciliation.set(null); this.resetReasons(); }
          await this.loadReconciliation();
        }
      } else { this.closingMode.set(false); this.movements.set([]); this.reconciliation.set(null); this.resetReasons(); }
    } catch (error) { if (!stateLoaded) this.stateReadError.set(true); this.error.set(errorText(error)); }
    finally { this.loading.set(false); this.stateLoading.set(false); }
  }
  async loadReconciliation(): Promise<void> {
    const value = await this.api.reconciliation();
    if (this.reconciliation()?.shiftId !== value.shiftId) this.resetReasons();
    this.reconciliation.set(value);
    this.counted = Object.fromEntries(value.paymentMethods.map(method => [method.paymentMethodId, method.countedAmount]));
  }
  onCountedChange(): void {
    if (this.difference !== 0 && this.reasons() === null && !this.reasonsLoading() && !this.reasonsError()) void this.loadReasons();
  }
  async loadReasons(): Promise<void> {
    if (!this.session.has('CASH.SHIFT_MANAGE') || this.reasonsLoading()) return;
    this.reasonsLoading.set(true); this.reasonsError.set('');
    try {
      const available = (await this.api.closureDifferenceReasons()).filter(reason => reason.isActive);
      this.reasons.set(available);
      if (!available.some(reason => reason.id === this.reasonId)) this.reasonId = '';
    } catch (error) { this.reasons.set(null); this.reasonsError.set(errorText(error)); }
    finally { this.reasonsLoading.set(false); }
  }
  private resetReasons(): void { this.reasons.set(null); this.reasonsError.set(''); this.reasonId = ''; }
  async open(): Promise<void> {
    if (!this.validMoney(this.initialAmount, true) || this.busy()) return;
    await this.run(async () => { await this.api.open(this.initialAmount, this.openObservations.trim() || null); this.notice.set('Turno abierto.'); await this.refresh(); });
  }
  async move(): Promise<void> {
    if (!this.validMoney(this.amount) || !this.concept.trim() || this.busy()) return;
    await this.run(async () => {
      await this.api.movement(this.kind, this.amount, this.concept.trim(), this.state()?.ownOperatorSession?.id ?? null);
      this.amount = 0; this.concept = ''; this.notice.set('Movimiento registrado.'); await this.refresh();
    });
  }
  async startSession(): Promise<void> {
    if (!this.credential.trim() || this.busy()) return;
    await this.run(async () => { await this.api.startSession(this.credential.trim()); this.credential = ''; this.notice.set('Sesión iniciada.'); await this.refresh(); });
  }
  async endSession(): Promise<void> {
    const id = this.state()?.ownOperatorSession?.id; if (!id || this.busy()) return;
    await this.run(async () => { await this.api.endSession(id); this.notice.set('Sesión finalizada.'); await this.refresh(); });
  }
  async close(): Promise<void> {
    const rec = this.reconciliation();
    if (!rec || !this.session.has('CASH.SHIFT_MANAGE') || this.busy() || !this.hasValidCounts) return;
    const hasDifference = this.difference !== 0;
    if (hasDifference) {
      if (this.reasons() === null || this.reasonsLoading() || this.reasonsError()) { this.error.set('No se pudieron cargar los motivos. Reintenta la consulta.'); return; }
      if (!this.reasons()!.some(reason => reason.id === this.reasonId)) { this.error.set('Selecciona un motivo de cierre.'); return; }
    }
    if (hasDifference) {
      await this.authorization.open({ title: 'Autorizar cierre con diferencia',
        description: 'Se cerrará el turno y quedará registrada la diferencia del arqueo con su motivo.',
        detail: `Turno ${rec.shiftId} · diferencia ${this.difference} COP`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
        onError: message => this.error.set(message),
        run: credentials => this.submitClosure(rec, credentials) });
    } else await this.submitClosure(rec, null);
  }
  private async submitClosure(rec: Reconciliation, credentials: AuthorizationCredentials | null): Promise<void> {
    const hasDifference = this.difference !== 0;
    await this.run(async () => {
      await this.api.close(rec.shiftId, {
        version: rec.shiftVersion,
        paymentMethods: this.methods().map(m => ({ paymentMethodId: m.paymentMethodId, countedAmount: Number(this.counted[m.paymentMethodId]) })),
        differenceReasonId: hasDifference ? this.reasonId : null,
        differenceObservation: hasDifference ? this.differenceObservation.trim() || null : null,
        authorization: hasDifference ? credentials : null,
      });
      this.closingMode.set(false); this.notice.set('Turno cerrado.'); await this.refresh();
    }, !!credentials);
  }
  private async run(action: () => Promise<void>, propagate = false): Promise<void> {
    this.busy.set(true); this.error.set(''); this.notice.set('');
    try { await action(); } catch (error) { this.error.set(errorText(error)); if (propagate) throw error; }
    finally { this.busy.set(false); }
  }
}
