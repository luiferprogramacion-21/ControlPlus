import { groupedNumber, NumberInput } from '../../shared/number-input';
import { readableCode } from '../../core/readable-code';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { errorText } from '../../core/api';
import { EstablishmentService } from '../../core/establishment';
import { SessionService } from '../../core/session.service';
import { CashApi, CashDrawerStatus, CashMovement, CashState, ClosureDifferenceReason, Reconciliation } from './cash-api';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody } from '../../shared/authorization-dialog';
import { statusKind, toastSignal } from '../../core/toast';
import { cashDrawerNotice } from '../../shared/cash-drawer-feedback';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { OperatorCard } from '../../shared/operator-card';
import { CashCorrections } from './cash-corrections';

@Component({
  selector: 'cp-cash',
  imports: [CashCorrections, NumberInput, FormsModule, CurrencyPipe, DatePipe, MatButtonModule],
  templateUrl: './cash.html',
  styleUrl: './cash.scss',
})
export class Cash implements OnInit {
  readonly readableCode = readableCode;
  readonly api = inject(CashApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly operatorCard = inject(OperatorCard);
  private readonly confirmations = inject(ConfirmationDialog);
  private readonly destroyRef = inject(DestroyRef);
  readonly session = inject(SessionService);
  readonly establishment = inject(EstablishmentService);
  readonly state = signal<CashState | null>(null);
  readonly reconciliation = signal<Reconciliation | null>(null);
  readonly closingMode = signal(false);
  /** Cerrar caja en pasos (decisión del 19 de noviembre de 2026): 1 contar, 2 revisar la diferencia, 3 confirmar. */
  readonly closeStep = signal<1 | 2 | 3>(1);
  readonly closeSteps = [{ number: 1, label: 'Contar el efectivo' }, { number: 2, label: 'Revisar la diferencia' }, { number: 3, label: 'Confirmar' }] as const;
  readonly movements = signal<CashMovement[]>([]);
  readonly loading = signal(false);
  readonly stateLoading = signal(true);
  readonly stateReadError = signal(false);
  readonly busy = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly reasons = signal<ClosureDifferenceReason[] | null>(null);
  readonly reasonsLoading = signal(false);
  readonly reasonsError = toastSignal('error');
  readonly drawer = signal<CashDrawerStatus | null>(null);
  readonly drawerLoading = signal(false);
  readonly drawerError = signal('');
  readonly drawerUncertain = signal(false);
  /** Resultado de la orden al cajón después de un movimiento de efectivo; el movimiento ya quedó registrado. */
  readonly drawerNotice = toastSignal(statusKind);
  drawerReason = '';
  initialAmount = 0;
  openObservations = '';
  /** Modalidad elegida al abrir; nula usa la predeterminada del negocio (individual si no hay otra). */
  openMode: 'INDIVIDUAL' | 'COMPARTIDO' | null = null;
  kind: 'incomes' | 'expenses' | 'cash-drops' = 'incomes';
  amount = 0;
  concept = '';
  credential = '';
  counted: Record<string, number | null> = {};
  /** Propuesta 4 (21 de noviembre de 2026): contar el efectivo por billete; el sistema suma y pone el total en «Contado». */
  readonly BILLS = [100_000, 50_000, 20_000, 10_000, 5_000, 2_000] as const;
  billCounts: Record<number, number | null> = {};
  get billTotal(): number {
    return this.BILLS.reduce((sum, bill) => sum + bill * (Number(this.billCounts[bill]) || 0), 0);
  }
  /** Cada cambio en la tabla de billetes reemplaza el valor contado del efectivo. */
  onBillCountChange(methodId: string): void { this.counted[methodId] = this.billTotal; this.onCountedChange(); }
  reasonId = '';
  differenceObservation = '';
  ngOnInit(): void { void this.establishment.load().catch(() => undefined); void this.refresh(); }
  modeLabel(mode: string | null | undefined): string {
    return mode === 'INDIVIDUAL' ? 'Individual' : mode === 'COMPARTIDO' ? 'Compartido' : 'No disponible';
  }
  configuredModeLabel(mode: string | null | undefined = this.state()?.defaultShiftMode): string {
    if (this.stateLoading()) return 'Cargando…';
    if (this.stateReadError()) return 'No disponible';
    return this.modeLabel(mode);
  }
  get difference(): number { return this.hasValidCounts ? this.cashMethods().reduce((sum, method) => sum + (Number(this.counted[method.paymentMethodId]) - method.expectedAmount), 0) : 0; }
  get hasValidCounts(): boolean { return this.cashMethods().length > 0 && this.cashMethods().every(method => {
    const value = this.counted[method.paymentMethodId];
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
  }); }
  methods() { return this.reconciliation()?.paymentMethods ?? []; }
  /** Only the physical cash is counted by hand. */
  cashMethods() { return this.methods().filter(method => method.affectsCash); }
  /** Nequi, transfers and other media cannot be verified by hand: the system total is shown for information. */
  electronicMethods() { return this.methods().filter(method => !method.affectsCash); }
  get electronicTotal(): number { return this.electronicMethods().reduce((sum, method) => sum + method.expectedAmount, 0); }
  get cashExpected(): number { return this.cashMethods().reduce((sum, method) => sum + method.expectedAmount, 0); }
  methodDifference(id: string, expected: number): number { return typeof this.counted[id] === 'number' ? Number(this.counted[id]) - expected : 0; }
  validMoney(value: number, allowZero = false): boolean { return Number.isSafeInteger(value) && (allowZero ? value >= 0 : value > 0); }
  async refresh(): Promise<void> {
    this.loading.set(true); this.stateLoading.set(true); this.error.set('');
    let stateLoaded = false;
    try {
      const state = await this.api.state(); stateLoaded = true; this.stateLoading.set(false); this.stateReadError.set(false); this.state.set(state); if (!state.hasOpenShift && !this.initialAmount && state.lastInitialAmount) this.initialAmount = state.lastInitialAmount;
      if (state.hasOpenShift) {
        this.movements.set((await this.api.movements()).items);
        if (this.canClose && state.currentShift) {
          if (this.canManageCash) await this.loadDrawerStatus();
          if (this.reconciliation()?.shiftId !== state.currentShift.id) { this.reconciliation.set(null); this.resetReasons(); }
          await this.loadReconciliation();
        }
      } else { this.closingMode.set(false); this.movements.set([]); this.reconciliation.set(null); this.drawer.set(null); this.drawerUncertain.set(false); this.resetReasons(); }
    } catch (error) { if (!stateLoaded) this.stateReadError.set(true); this.error.set(errorText(error)); }
    finally { this.loading.set(false); this.stateLoading.set(false); }
  }
  async loadDrawerStatus(): Promise<void> {
    this.drawerLoading.set(true); this.drawerError.set('');
    try { this.drawer.set(await this.api.drawerStatus()); }
    catch (error) { this.drawer.set(null); this.drawerError.set(errorText(error)); }
    finally { this.drawerLoading.set(false); }
  }
  async openDrawer(): Promise<void> {
    const shiftId = this.state()?.currentShift?.id;
    const reason = this.drawerReason.trim();
    if (!shiftId || !this.session.has('CASH.SHIFT_MANAGE') || !this.drawer()?.canOpen ||
      this.drawerUncertain() || this.busy() || !reason || reason.length > 300) return;
    const approved = await this.confirmations.confirm({
      title: 'Enviar apertura al cajón',
      message: `Motivo: ${reason}\n\nLa impresora recibirá una orden de apertura. Comprueba físicamente si el cajón se abrió.`,
      confirmText: 'Enviar orden',
    }, this.destroyRef);
    if (!approved || this.destroyRef.destroyed || this.busy() || !this.drawer()?.canOpen ||
      this.state()?.currentShift?.id !== shiftId || !this.session.has('CASH.SHIFT_MANAGE') ||
      this.drawerReason.trim() !== reason) return;
    this.busy.set(true); this.error.set(''); this.notice.set('');
    try {
      const result = await this.api.openDrawer(reason, crypto.randomUUID());
      if (result.status === 'ENVIADO') {
        this.notice.set('Orden enviada a la impresora. Comprueba si el cajón se abrió.');
        this.drawerReason = '';
      } else {
        this.drawerUncertain.set(true);
        this.error.set(result.message || 'No se confirmó la apertura. Comprueba el cajón antes de solicitar otra orden.');
      }
    } catch (error) {
      this.drawerUncertain.set(true);
      this.error.set(`No se pudo confirmar el resultado: ${errorText(error)} Comprueba el cajón antes de solicitar otra orden.`);
    } finally { this.busy.set(false); }
  }
  prepareNewDrawerOpen(): void {
    if (this.busy()) return;
    this.drawerUncertain.set(false); this.drawerReason = ''; this.error.set('');
  }
  async loadReconciliation(): Promise<void> {
    const value = await this.api.reconciliation();
    if (this.reconciliation()?.shiftId !== value.shiftId) { this.resetReasons(); this.billCounts = {}; }
    this.reconciliation.set(value);
    this.counted = Object.fromEntries(value.paymentMethods.filter(method => method.affectsCash).map(method => [method.paymentMethodId, method.countedAmount]));
  }
  onCountedChange(): void {
    if (this.difference !== 0 && this.reasons() === null && !this.reasonsLoading() && !this.reasonsError()) void this.loadReasons();
  }
  async loadReasons(): Promise<void> {
    if (!this.session.has('CASH.SHIFT_MANAGE') || this.reasonsLoading()) return;
    this.reasonsLoading.set(true); this.reasonsError.set('');
    try {
      let available = (await this.api.closureDifferenceReasons()).filter(reason => reason.isActive);
      // A fresh installation has no reasons yet: the default ones are created once so the cashier can pick one.
      if (!available.length) {
        try { available = (await this.api.initializeClosureReasons()).filter(reason => reason.isActive); }
        catch { /* The list stays empty and the screen shows its own message. */ }
      }
      this.reasons.set(available);
      if (!available.some(reason => reason.id === this.reasonId)) this.reasonId = '';
    } catch (error) { this.reasons.set(null); this.reasonsError.set(errorText(error)); }
    finally { this.reasonsLoading.set(false); }
  }
  openModeFor(cash: CashState | null = this.state()): 'INDIVIDUAL' | 'COMPARTIDO' {
    return this.openMode ?? (cash?.defaultShiftMode === 'COMPARTIDO' ? 'COMPARTIDO' : 'INDIVIDUAL');
  }
  /** The Administrator closes with a difference without choosing a reason (the server records the generic one). */
  goToCloseStep(step: 1 | 2 | 3): void {
    if (step > 1 && !this.hasValidCounts) return;
    if (step === 2) this.onCountedChange();
    this.error.set('');
    this.closeStep.set(step);
  }
  get selectedReasonName(): string { return this.reasons()?.find(reason => reason.id === this.reasonId)?.name ?? ''; }
  /** Auditoría del 23/11/2026 (C.1): el Cajero cierra su propio turno individual con la autorización de un superior. */
  get canManageCash(): boolean { return this.session.has('CASH.SHIFT_MANAGE'); }
  get ownsShift(): boolean {
    const shift = this.state()?.currentShift;
    return !!shift && shift.mode === 'INDIVIDUAL' && !!shift.responsibleUserId && shift.responsibleUserId === this.session.user()?.id;
  }
  get canClose(): boolean { return this.canManageCash || this.ownsShift; }
  /** Falta el motivo (quien administra la caja) o la explicación (el Cajero) de una diferencia. */
  get blockedByReason(): boolean {
    if (this.difference === 0) return false;
    if (!this.canManageCash) return !this.differenceObservation.trim();
    return !this.isAdministrator && (this.reasonsLoading() || !this.reasons()?.length || !this.reasonId);
  }
  get isAdministrator(): boolean { return typeof this.session.hasRole === 'function' && this.session.hasRole('ADMINISTRADOR'); }
  private resetReasons(): void { this.reasons.set(null); this.reasonsError.set(''); this.reasonId = ''; }
  async open(): Promise<void> {
    // Decisión del 19 de noviembre de 2026: la caja no se abre con base $0.
    if (!this.validMoney(this.initialAmount) || this.busy()) { if (!this.busy()) this.error.set('Escribe el fondo inicial: la caja no se abre con $0.'); return; }
    if (this.session.has('CASH.SHIFT_MANAGE')) {
      await this.run(async () => { await this.api.open(this.initialAmount, this.openObservations.trim() || null, undefined, this.openModeFor()); this.openMode = null; this.notice.set('Turno abierto.'); await this.refresh(); });
      return;
    }
    // A Cajero opens the cash only with the authorization of a Supervisor or Administrador.
    await this.authorization.open({ title: 'Autorizar apertura de caja',
      description: 'Un Cajero necesita la autorización de un Supervisor o Administrador para abrir la caja.',
      detail: `Fondo inicial ${groupedNumber(this.initialAmount)} COP`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      onError: message => this.error.set(message),
      run: credentials => this.submitOpen(credentials) });
  }
  private async submitOpen(credentials: AuthorizationCredentials | null): Promise<void> {
    if (!credentials) return;
    await this.run(async () => {
      await this.api.open(this.initialAmount, this.openObservations.trim() || null, authorizationBody(credentials), this.openModeFor());
      this.openMode = null;
      this.notice.set('Turno abierto.'); await this.refresh();
    }, true);
  }
  /**
   * Pruebas del usuario del 24/11/2026 (punto 13): todo movimiento (ingreso, gasto o retiro) pide autorizador. El Administrador
   * lo registra solo; el Supervisor y (25/11/2026) también el Cajero con permiso, con el Administrador.
   * 25/11/2026: el botón ya no se bloquea en turno compartido. Si nadie ha pasado su carné, se pide antes de la autorización y
   * decide el rol de su dueño, porque el movimiento queda a su nombre.
   */
  readonly movementKinds = { incomes: 'Ingreso', expenses: 'Gasto', 'cash-drops': 'Retiro de efectivo' } as const;
  private movementAsked = false;
  async move(): Promise<void> {
    if (!this.validMoney(this.amount) || !this.concept.trim() || this.busy() || this.movementAsked) return;
    const detail = `${this.movementKinds[this.kind]} de ${groupedNumber(this.amount)} COP · ${this.concept.trim()}`;
    this.movementAsked = true;
    try {
      const operator = await this.operatorCard.ensure(detail);
      if (!operator) return;
      if (operator.shared) await this.refresh();
      const role = operator.shared ? operator.role : this.loggedRole;
      if (role === 'ADMINISTRADOR') { await this.submitMove(null, operator.sessionId); return; }
      // 25/11/2026: los movimientos de caja solo los autoriza el Administrador, también los del Cajero.
      await this.authorization.open({ title: 'Autorizar movimiento de caja',
        description: 'Los movimientos de caja los autoriza el Administrador (su carné, o su usuario y contraseña).',
        detail, operatorUserName: operator.shared ? '' : this.session.user()?.userName ?? '', requireDifferentUser: !operator.shared, allowCredential: true,
        onError: message => this.error.set(message),
        run: credentials => this.submitMove(credentials, operator.sessionId) });
    } finally { this.movementAsked = false; }
  }
  private get loggedRole(): string {
    return this.isAdministrator ? 'ADMINISTRADOR' : typeof this.session.hasRole === 'function' && this.session.hasRole('SUPERVISOR') ? 'SUPERVISOR' : 'CAJERO';
  }
  private async submitMove(credentials: AuthorizationCredentials | null, sessionId: string | null): Promise<void> {
    await this.run(async () => {
      const movement = await this.api.movement(this.kind, this.amount, this.concept.trim(), sessionId,
        credentials ? authorizationBody(credentials) : null);
      this.amount = 0; this.concept = ''; this.notice.set('Movimiento registrado.');
      this.drawerNotice.set(cashDrawerNotice(movement.drawerStatus));
      await this.refresh();
    }, !!credentials);
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
    if (!rec || !this.canClose || this.busy() || !this.hasValidCounts) return;
    const hasDifference = this.difference !== 0;
    if (!this.canManageCash) {
      if (hasDifference && !this.differenceObservation.trim()) { this.error.set('Escribe qué pasó con la diferencia de la caja.'); return; }
      await this.authorization.open({ title: 'Autorizar cierre de caja',
        description: 'Un Cajero cierra la caja con la autorización de un Supervisor o del Administrador. El turno termina y ya no se puede vender hasta abrir otro.',
        detail: `Diferencia ${groupedNumber(this.difference)} COP`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
        onError: message => this.error.set(message),
        run: credentials => this.submitClosure(rec, credentials) });
      return;
    }
    if (hasDifference && !this.isAdministrator) {
      if (this.reasons() === null || this.reasonsLoading() || this.reasonsError()) { this.error.set('No se pudieron cargar los motivos. Reintenta la consulta.'); return; }
      if (!this.reasons()!.some(reason => reason.id === this.reasonId)) { this.error.set('Selecciona un motivo de cierre.'); return; }
    }
    if (hasDifference) {
      await this.authorization.open({ title: 'Autorizar cierre con diferencia',
        description: 'Se cerrará el turno y quedará registrada la diferencia del arqueo con su motivo.',
        detail: `Turno ${rec.shiftId} · diferencia ${this.difference} COP`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
        onError: message => this.error.set(message),
        run: credentials => this.submitClosure(rec, credentials) });
    } else await this.submitClosure(rec, null);
  }
  private async submitClosure(rec: Reconciliation, credentials: AuthorizationCredentials | null): Promise<void> {
    const hasDifference = this.difference !== 0;
    await this.run(async () => {
      await this.api.close(rec.shiftId, {
        version: rec.shiftVersion,
        paymentMethods: this.cashMethods().map(m => ({ paymentMethodId: m.paymentMethodId, countedAmount: Number(this.counted[m.paymentMethodId]) })),
        differenceReasonId: hasDifference && this.canManageCash ? this.reasonId || null : null,
        differenceObservation: hasDifference ? this.differenceObservation.trim() || null : null,
        authorization: credentials && (hasDifference || !this.canManageCash) ? authorizationBody(credentials) : null,
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
