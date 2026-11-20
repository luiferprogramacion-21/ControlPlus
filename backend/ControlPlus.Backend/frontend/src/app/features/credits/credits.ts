import { NumberInput } from '../../shared/number-input';
import { cashDrawerNotice } from '../../shared/cash-drawer-feedback';
import { A11yModule } from '@angular/cdk/a11y';
import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { CustomersApi, Customer } from '../customers/customers-api';
import { Icon } from '../../layout/icon';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { Credit, CreditPayment, CreditPaymentCancellationReason, CreditsApi, PaymentMethodInput } from './credits-api';
import { statusKind, toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';

type Method = PaymentMethodInput['methodCode'];
type Draft = { methodCode: Method; appliedAmount: number; receivedAmount: number | null; reference: string; };
const LIMIT = Number.MAX_SAFE_INTEGER;
@Component({ selector: 'cp-credits', imports: [WindowSheet, NumberInput, A11yModule, DatePipe, FormsModule, MatPaginatorModule, Icon, RouterLink], templateUrl: './credits.html', styleUrls: ['../directory.scss', './credits.scss'] })
export class Credits implements OnInit {
  private readonly api = inject(CreditsApi);
  private readonly customersApi = inject(CustomersApi);
  private readonly authorization = inject(AuthorizationDialog);
  readonly session = inject(SessionService);
  readonly result = signal<Page<Credit> | null>(null);
  readonly selected = signal<Credit | null>(null);
  readonly detailOpen = signal(false);
  readonly history = signal<Page<CreditPayment> | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly drawerNotice = toastSignal(statusKind);
  readonly dialog = signal(false);
  readonly cancellingPayment = signal(false);
  readonly forgiving = signal(false);
  readonly cancellationReasons = signal<CreditPaymentCancellationReason[]>([]);
  readonly cancellationTarget = signal<CreditPayment | null>(null);
  readonly customerMatches = signal<Customer[]>([]);
  readonly customerLabel = signal('');
  state = ''; saleNumber = ''; dueFrom = ''; dueTo = ''; customerSearch = ''; customerId = '';
  page = 1; pageSize = 20; historyPage = 1; historyPageSize = 20;
  drafts: Draft[] = [this.emptyDraft()];
  cancellationReasonId = ''; cancellationObservation = ''; forgivenessReason = '';
  private requestId = 0;
  private pendingKey: string | null = null;
  private pendingFingerprint = '';
  private pendingCancellation: { key: string; fingerprint: string; authorizer: string | null } | null = null;
  private pendingForgiveness: { key: string; fingerprint: string } | null = null;
  get canReadHistory(): boolean { return this.session.has('CUSTOMERS.HISTORY_READ'); }
  get canManageLifecycle(): boolean { return this.session.has('CREDIT.MANAGE') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR'); }
  get canForgive(): boolean { const credit = this.selected(); return !!credit && credit.balance > 0 && credit.state !== 'ANULADO' && this.session.has('CREDIT.MANAGE') && this.session.hasRole('ADMINISTRADOR'); }
  get canFindCustomers(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }
  get appliedTotal(): number { return this.drafts.reduce((sum, draft) => sum + Number(draft.appliedAmount || 0), 0); }
  get change(): number { const cash = this.drafts.find(d => d.methodCode === 'EFECTIVO'); return cash ? Math.max(0, Number(cash.receivedAmount || 0) - Number(cash.appliedAmount || 0)) : 0; }
  ngOnInit(): void { void this.reload(); }
  money(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value); }
  paid(credit: Credit): number { return credit.adjustedAmount - credit.balance; }
  label(state: string): string { return ({ PENDIENTE: 'Pendiente', VENCIDO: 'Vencido', VENCIDA: 'Vencida', PAGADO: 'Pagado', PAGADA: 'Pagada', ANULADO: 'Anulado', ANULADA: 'Anulada', APLICADO: 'Aplicado' } as Record<string, string>)[state] ?? state; }
  private emptyDraft(): Draft { return { methodCode: 'EFECTIVO', appliedAmount: 0, receivedAmount: null, reference: '' }; }
  async reload(): Promise<void> {
    const id = ++this.requestId; this.loading.set(true); this.error.set('');
    try {
      const result = await this.api.list({ page: this.page, pageSize: this.pageSize, state: this.state || undefined, saleNumber: this.saleNumber.trim() || undefined, dueFrom: this.dueFrom || undefined, dueTo: this.dueTo || undefined, customerId: this.customerId || undefined });
      if (id === this.requestId) this.result.set(result);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }
  find(): void {
    if (this.saleNumber.trim().length > 40 || (this.dueFrom && this.dueTo && this.dueFrom > this.dueTo)) { this.error.set('Revisa el número de venta y el intervalo de vencimiento.'); return; }
    this.page = 1; void this.reload();
  }
  pageChanged(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  async findCustomer(): Promise<void> {
    if (!this.canFindCustomers || !this.customerSearch.trim()) return;
    try { const result = await this.customersApi.list({ search: this.customerSearch.trim(), status: 'all', page: 1, pageSize: 20 }); this.customerMatches.set(result.items); this.error.set(''); }
    catch (e) { this.error.set(errorText(e)); }
  }
  selectCustomer(customer: Customer): void { this.customerId = customer.id; this.customerLabel.set(customer.fullName); this.customerSearch = customer.fullName; this.customerMatches.set([]); this.find(); }
  clearCustomer(): void { this.customerId = ''; this.customerSearch = ''; this.customerLabel.set(''); this.customerMatches.set([]); this.find(); }
  async open(credit: Credit): Promise<void> {
    this.selected.set(credit); this.detailOpen.set(true); this.history.set(null); this.historyPage = 1; this.error.set(''); this.notice.set('');
    await this.refreshSelected();
  }
  async refreshSelected(): Promise<void> {
    const id = this.selected()?.id; if (!id) return;
    try {
      const credit = await this.api.get(id); if (this.selected()?.id !== id) return;
      this.selected.set(credit);
      if (this.canReadHistory) await this.loadHistory();
    } catch (e) { this.error.set(errorText(e)); }
  }
  async loadHistory(): Promise<void> {
    const id = this.selected()?.id; if (!id || !this.canReadHistory) return;
    try { const history = await this.api.payments(id, this.historyPage, this.historyPageSize); if (this.selected()?.id === id) this.history.set(history); }
    catch (e) { this.error.set(errorText(e)); }
  }
  historyChanged(e: PageEvent): void { this.historyPage = e.pageIndex + 1; this.historyPageSize = e.pageSize; void this.loadHistory(); }
  close(): void { if (this.saving()) return; this.selected.set(null); this.detailOpen.set(false); this.dialog.set(false); this.cancellingPayment.set(false); this.forgiving.set(false); this.history.set(null); this.pendingKey = null; this.pendingCancellation = null; this.pendingForgiveness = null; this.error.set(''); }
  openPayment(credit: Credit): void { if (credit.state === 'PAGADO' || credit.state === 'ANULADO' || credit.balance <= 0) return; this.selected.set(credit); this.drafts = [this.emptyDraft()]; this.pendingKey = null; this.dialog.set(true); this.error.set(''); }
  addMethod(): void { if (this.drafts.length < 3) { const next = (['EFECTIVO', 'TRANSFERENCIA', 'NEQUI'] as Method[]).find(m => !this.drafts.some(d => d.methodCode === m)); if (next) this.drafts.push({ methodCode: next, appliedAmount: 0, receivedAmount: null, reference: '' }); } }
  removeMethod(index: number): void { if (this.drafts.length > 1) this.drafts.splice(index, 1); }
  validation(): string {
    const credit = this.selected(); if (!credit) return 'Selecciona un crédito.';
    if (credit.state === 'PAGADO' || credit.state === 'ANULADO' || credit.balance <= 0) return 'Este crédito no admite más abonos.';
    if (new Set(this.drafts.map(d => d.methodCode)).size !== this.drafts.length) return 'Cada medio de pago se usa una sola vez.';
    if (this.drafts.some(d => !Number.isSafeInteger(Number(d.appliedAmount)) || d.appliedAmount <= 0 || d.appliedAmount > LIMIT)) return 'Cada importe debe ser un valor entero mayor que cero.';
    if (!Number.isSafeInteger(this.appliedTotal) || this.appliedTotal > credit.balance) return 'El abono supera el saldo pendiente.';
    for (const d of this.drafts) {
      if (d.methodCode === 'EFECTIVO' && (!Number.isSafeInteger(Number(d.receivedAmount)) || Number(d.receivedAmount) < Number(d.appliedAmount) || Number(d.receivedAmount) > LIMIT)) return 'El efectivo recibido debe cubrir el importe aplicado.';
      if (d.methodCode !== 'EFECTIVO' && (!d.reference.trim() || d.reference.trim().length > 200)) return 'Ingresa una referencia de hasta 200 caracteres para el pago electrónico.';
    }
    return '';
  }
  async save(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    const credit = this.selected()!;
    const payments: PaymentMethodInput[] = this.drafts.map(d => ({ methodCode: d.methodCode, appliedAmount: Number(d.appliedAmount), receivedAmount: d.methodCode === 'EFECTIVO' ? Number(d.receivedAmount) : null, reference: d.methodCode === 'EFECTIVO' ? null : d.reference.trim() }));
    const fingerprint = JSON.stringify({ id: credit.id, version: credit.version, payments });
    if (fingerprint !== this.pendingFingerprint) { this.pendingKey = crypto.randomUUID(); this.pendingFingerprint = fingerprint; }
    this.saving.set(true); this.error.set('');
    try {
      const result = await this.api.applyPayment(credit, payments, this.pendingKey!);
      this.pendingKey = null; this.pendingFingerprint = ''; this.dialog.set(false); this.notice.set('Abono registrado.');
      this.drawerNotice.set(cashDrawerNotice(result.drawerStatus));
      await this.refreshSelected(); await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  canCancel(payment: CreditPayment): boolean { return this.canManageLifecycle && payment.state === 'APLICADO'; }
  get selectedCancellationReason(): CreditPaymentCancellationReason | undefined { return this.cancellationReasons().find(x => x.id === this.cancellationReasonId); }
  async startCancelPayment(payment: CreditPayment): Promise<void> {
    if (!this.canCancel(payment) || this.saving()) return;
    this.error.set(''); this.cancellationTarget.set(payment); this.cancellationReasonId = ''; this.cancellationObservation = ''; this.pendingCancellation = null;
    try {
      this.cancellationReasons.set(await this.api.paymentCancellationReasons());
      if (!this.cancellationReasons().length) { this.error.set('No hay motivos para anular pagos. Pide al Administrador que los cree en Configuración.'); this.cancellationTarget.set(null); return; }
      this.cancellingPayment.set(true);
    } catch (e) { this.cancellationTarget.set(null); this.error.set(errorText(e)); }
  }
  cancelPaymentValidation(): string {
    if (!this.cancellationReasonId) return 'Selecciona el motivo de la anulación.';
    if (this.selectedCancellationReason?.code === 'OTRO' && !this.cancellationObservation.trim()) return 'La observación es obligatoria cuando el motivo es «Otro».';
    if (this.cancellationObservation.trim().length > 500) return 'La observación admite hasta 500 caracteres.';
    return '';
  }
  async confirmCancelPayment(): Promise<void> {
    const credit = this.selected(); const payment = this.cancellationTarget();
    if (!credit || !payment || this.saving()) return;
    const issue = this.cancelPaymentValidation(); if (issue) { this.error.set(issue); return; }
    await this.authorization.open({
      title: 'Autorizar anulación de abono',
      description: 'Se restituirá el saldo y se registrará el reverso en el turno de Caja abierto. Si el abono usó varios medios, se anularán juntos.',
      detail: `${payment.number} · ${this.money(payment.appliedAmount)}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true,
      allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
      onError: message => this.error.set(message),
      run: credentials => this.submitCancellation(credit, payment, credentials),
    });
  }
  private async submitCancellation(credit: Credit, payment: CreditPayment, credentials: AuthorizationCredentials | null): Promise<void> {
    const body = { expectedVersion: credit.version, reasonId: this.cancellationReasonId, observation: this.cancellationObservation.trim() || null };
    const fingerprint = JSON.stringify({ creditId: credit.id, paymentId: payment.id, body });
    const authorizer = authorizationIdentity(credentials);
    if (this.pendingCancellation && this.pendingCancellation.fingerprint === fingerprint && this.pendingCancellation.authorizer !== authorizer)
      throw new Error('Reintenta con el mismo Administrador autorizador.');
    if (!this.pendingCancellation || this.pendingCancellation.fingerprint !== fingerprint)
      this.pendingCancellation = { key: crypto.randomUUID(), fingerprint, authorizer };
    this.saving.set(true); this.error.set('');
    try {
      const result = await this.api.cancelPayment(credit, payment.id,
        credentials ? { ...body, authorization: authorizationBody(credentials) } : body,
        this.pendingCancellation.key);
      this.pendingCancellation = null; this.cancellingPayment.set(false); this.cancellationTarget.set(null);
      this.notice.set(`Abono anulado. Se restituyeron ${this.money(result.reversedAmount)} al saldo.`);
      await this.refreshSelected(); await this.reload();
    } catch (e) { throw e; }
    finally { this.saving.set(false); }
  }
  startForgive(): void {
    if (!this.canForgive || this.saving()) return;
    this.forgivenessReason = ''; this.pendingForgiveness = null; this.error.set(''); this.forgiving.set(true);
  }
  async confirmForgive(): Promise<void> {
    const credit = this.selected(); const reason = this.forgivenessReason.trim();
    if (!credit || !this.canForgive || this.saving()) return;
    if (!reason || reason.length > 500) { this.error.set('Escribe un motivo de hasta 500 caracteres.'); return; }
    const fingerprint = JSON.stringify({ creditId: credit.id, version: credit.version, reason });
    if (!this.pendingForgiveness || this.pendingForgiveness.fingerprint !== fingerprint)
      this.pendingForgiveness = { key: crypto.randomUUID(), fingerprint };
    this.saving.set(true); this.error.set('');
    try {
      await this.api.forgive(credit, reason, this.pendingForgiveness.key);
      this.pendingForgiveness = null; this.forgiving.set(false);
      this.notice.set('Deuda perdonada. El crédito quedó anulado; la venta y sus abonos se conservaron.');
      await this.refreshSelected(); await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
