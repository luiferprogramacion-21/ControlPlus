import { groupedNumber } from '../../shared/number-input';
import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { Customer, CustomersApi } from '../customers/customers-api';
import { CreatedLayaway, Layaway, LayawayCancellation, LayawayDetail, LayawayPayment, LayawayPaymentCancellationReason, LayawayPaymentInput, LayawayProductCorrection, LayawaysApi } from './layaways-api';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { LayawayCancel } from './layaway-cancel';
import { LayawayProducts } from './layaway-products';
import { LayawayCreate } from './layaway-create';
import { appliedTotal, cashChange, emptyDraft, PaymentDraft, PaymentMethods, toPayments, validateDrafts } from './payment-methods';
import { statusKind, toastSignal } from '../../core/toast';
import { cashDrawerNotice } from '../../shared/cash-drawer-feedback';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { WindowSheet } from '../../shared/window-sheet';
import { ActivatedRoute } from '@angular/router';

const STATE_LABELS: Record<string, string> = { ACTIVO: 'Activo', PAGADO: 'Pagado', ENTREGADO: 'Entregado', ANULADO: 'Anulado' };

@Component({
  selector: 'cp-layaways',
  imports: [WindowSheet, DatePipe, FormsModule, MatPaginatorModule, Icon, PaymentMethods, LayawayCreate, LayawayCancel, LayawayProducts],
  templateUrl: './layaways.html',
  styleUrls: ['../directory.scss', './layaways.scss'],
})
export class Layaways implements OnInit {
  private readonly api = inject(LayawaysApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly confirmation = inject(ConfirmationDialog);
  private readonly owner = inject(DestroyRef);
  private readonly customersApi = inject(CustomersApi);
  readonly session = inject(SessionService);
  readonly number = groupedNumber;
  readonly result = signal<Page<Layaway> | null>(null);
  readonly detail = signal<LayawayDetail | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly drawerNotice = toastSignal(statusKind);
  readonly paymentDialog = signal(false);
  readonly creating = signal(false);
  readonly cancelling = signal(false);
  /** Corregir productos de un apartado sin entregar (decisión del 19 de noviembre de 2026). */
  readonly correctingProducts = signal(false);
  readonly customerMatches = signal<Customer[]>([]);
  readonly customerLabel = signal('');
  readonly correctingPayment = signal<LayawayPayment | null>(null);
  readonly paymentReasons = signal<LayawayPaymentCancellationReason[]>([]);
  paymentReasonId = ''; paymentObservation = '';
  private cancellationAttempt: { fingerprint: string; key: string; authorizer: string | null } | null = null;
  state = ''; overdue: '' | 'true' | 'false' = ''; dueFrom = ''; dueTo = ''; customerSearch = ''; customerId = '';
  page = 1; pageSize = 20;
  drafts: PaymentDraft[] = [emptyDraft()];
  private requestId = 0;
  private paymentKey: string | null = null; private paymentFingerprint = '';
  private deliveryKey: string | null = null; private deliveryFingerprint = '';

  get canFindCustomers(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }
  get appliedTotal(): number { return appliedTotal(this.drafts); }
  get change(): number { return cashChange(this.drafts); }

  private readonly route = inject(ActivatedRoute, { optional: true });
  ngOnInit(): void {
    void this.reload();
    // Desde Inicio, «Nuevo apartado» abre directamente la ventana para crearlo.
    if (this.route?.snapshot.queryParamMap.get('accion') === 'nuevo') this.startCreate();
  }
  money(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value); }
  label(state: string): string { return STATE_LABELS[state] ?? state; }
  paid(layaway: Layaway): number { return layaway.total - layaway.balance; }
  canPay(layaway: Layaway): boolean { return layaway.state === 'ACTIVO' && layaway.balance > 0; }
  /** Cancelling is an Administrator decision and needs both effective permissions. */
  canCancel(layaway: Layaway): boolean {
    return (layaway.state === 'ACTIVO' || layaway.state === 'PAGADO') && this.session.has('LAYAWAY.CANCEL') && this.session.has('LAYAWAY.REFUND_DECIDE') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR');
  }
  canDeliver(layaway: Layaway): boolean { return layaway.state === 'PAGADO' && layaway.balance === 0; }
  canCorrectPayment(payment: LayawayPayment): boolean {
    const layaway = this.detail()?.layaway;
    return !!layaway && ['ACTIVO', 'PAGADO'].includes(layaway.state) && payment.state === 'APLICADO' &&
      this.session.has('LAYAWAY.MANAGE') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR');
  }
  get correctingPaymentTotal(): number {
    const target = this.correctingPayment();
    return target ? this.detail()?.payments.filter(p => p.state === 'APLICADO' &&
      (p.id === target.id || (!!target.correlationId && p.correlationId === target.correlationId)))
      .reduce((sum, p) => sum + p.appliedAmount, 0) ?? 0 : 0;
  }
  async startCorrectPayment(payment: LayawayPayment): Promise<void> {
    if (!this.canCorrectPayment(payment) || this.saving()) return;
    this.paymentReasonId = ''; this.paymentObservation = ''; this.cancellationAttempt = null; this.error.set('');
    try {
      this.paymentReasons.set(await this.api.paymentCancellationReasons());
      if (!this.paymentReasons().length) { this.error.set('No hay motivos activos para anular el abono.'); return; }
      this.correctingPayment.set(payment);
    } catch (e) { this.error.set(errorText(e)); }
  }
  closePaymentCorrection(): void { if (!this.saving()) this.correctingPayment.set(null); }
  async confirmPaymentCorrection(): Promise<void> {
    const layaway = this.detail()?.layaway; const payment = this.correctingPayment();
    if (!layaway || !payment || this.saving() || !this.canCorrectPayment(payment)) return;
    const reason = this.paymentReasons().find(r => r.id === this.paymentReasonId);
    if (!reason || (reason.code === 'OTRO' && !this.paymentObservation.trim()) || this.paymentObservation.trim().length > 500) {
      this.error.set('Selecciona un motivo; Otro requiere una observación de hasta 500 caracteres.'); return;
    }
    await this.authorization.open({ title: 'Corregir abono de apartado',
      description: 'Se anula el abono completo con sus medios originales en la caja actual. Los productos siguen reservados. Después podrás registrar el abono correcto.',
      detail: `${payment.number} · ${this.money(this.correctingPaymentTotal)}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message),
      run: credentials => this.executePaymentCorrection(layaway, payment, credentials) });
  }
  private async executePaymentCorrection(layaway: Layaway, payment: LayawayPayment, credentials: AuthorizationCredentials | null): Promise<void> {
    const current = this.detail()?.layaway;
    if (this.owner.destroyed || this.saving() || current?.id !== layaway.id || current.version !== layaway.version ||
      this.correctingPayment()?.id !== payment.id || !this.canCorrectPayment(payment)) throw new Error('El apartado cambió. Vuelve a consultar el abono.');
    const body = { expectedVersion: layaway.version, reasonId: this.paymentReasonId, observation: this.paymentObservation.trim() || null };
    const fingerprint = JSON.stringify({ id: layaway.id, paymentId: payment.id, body });
    const authorizer = authorizationIdentity(credentials);
    if (this.cancellationAttempt?.fingerprint === fingerprint && this.cancellationAttempt.authorizer !== authorizer)
      throw new Error('Reintenta con el mismo Administrador autorizador.');
    if (this.cancellationAttempt?.fingerprint !== fingerprint) this.cancellationAttempt = { fingerprint, authorizer, key: crypto.randomUUID() };
    this.saving.set(true);
    try {
      const result = await this.api.cancelPayment(layaway, payment.id, credentials ? { ...body, authorization: authorizationBody(credentials) } : body, this.cancellationAttempt!.key);
      this.cancellationAttempt = null; this.correctingPayment.set(null);
      await this.refreshDetail(); await this.reload();
      this.drafts = [emptyDraft()]; this.paymentKey = null; this.paymentDialog.set(true);
      this.notice.set(`Abono anulado por ${this.money(result.reversedAmount)}. Los productos siguen reservados; registra ahora el abono correcto.`);
    } finally { this.saving.set(false); }
  }

  async reload(): Promise<void> {
    const id = ++this.requestId; this.loading.set(true); this.error.set('');
    try {
      const result = await this.api.list({
        page: this.page, pageSize: this.pageSize, state: this.state || undefined, customerId: this.customerId || undefined,
        dueFrom: this.dueFrom || undefined, dueTo: this.dueTo || undefined,
        overdue: this.overdue === '' ? undefined : this.overdue === 'true',
      });
      if (id === this.requestId) this.result.set(result);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }

  find(): void {
    if (this.dueFrom && this.dueTo && this.dueFrom > this.dueTo) { this.error.set('Revisa el intervalo de fecha límite.'); return; }
    this.page = 1; void this.reload();
  }
  pageChanged(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }

  async findCustomer(): Promise<void> {
    if (!this.canFindCustomers || !this.customerSearch.trim()) return;
    try {
      this.customerMatches.set((await this.customersApi.list({ search: this.customerSearch.trim(), status: 'all', page: 1, pageSize: 20 })).items);
      this.error.set('');
    } catch (e) { this.error.set(errorText(e)); }
  }
  selectCustomer(customer: Customer): void { this.customerId = customer.id; this.customerLabel.set(customer.fullName); this.customerSearch = customer.fullName; this.customerMatches.set([]); this.find(); }
  clearCustomer(): void { this.customerId = ''; this.customerSearch = ''; this.customerLabel.set(''); this.customerMatches.set([]); this.find(); }

  async open(layaway: Layaway): Promise<void> {
    this.error.set(''); this.notice.set('');
    await this.loadDetail(layaway.id);
  }
  async loadDetail(id: string): Promise<void> {
    try { this.detail.set(await this.api.get(id)); }
    catch (e) { this.error.set(errorText(e)); }
  }
  async refreshDetail(): Promise<void> { const id = this.detail()?.layaway.id; if (id) await this.loadDetail(id); }
  closeDetail(): void { if (this.saving()) return; this.detail.set(null); this.paymentDialog.set(false); this.paymentKey = null; this.deliveryKey = null; }

  /** The list row can be stale; payments and delivery always use the version of a freshly read detail. */
  async openPayment(layaway: Layaway): Promise<void> {
    if (!this.canPay(layaway)) return;
    this.error.set(''); this.notice.set('');
    await this.loadDetail(layaway.id);
    const current = this.detail()?.layaway;
    if (!current || !this.canPay(current)) return;
    this.drafts = [emptyDraft()]; this.paymentKey = null; this.paymentDialog.set(true);
  }
  closePayment(): void { if (!this.saving()) { this.paymentDialog.set(false); this.error.set(''); } }

  validation(): string {
    const layaway = this.detail()?.layaway; if (!layaway) return 'Selecciona un apartado.';
    if (!this.canPay(layaway)) return 'Este apartado no admite más abonos.';
    return validateDrafts(this.drafts, { mode: 'payment', max: layaway.balance, overMessage: 'El abono supera el saldo pendiente.' });
  }

  async savePayment(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    const layaway = this.detail()!.layaway;
    const payments: LayawayPaymentInput[] = toPayments(this.drafts);
    const fingerprint = JSON.stringify({ id: layaway.id, version: layaway.version, payments });
    if (fingerprint !== this.paymentFingerprint) { this.paymentKey = crypto.randomUUID(); this.paymentFingerprint = fingerprint; }
    this.saving.set(true); this.error.set('');
    try {
      const result = await this.api.applyPayment(layaway, payments, this.paymentKey!);
      this.paymentKey = null; this.paymentFingerprint = ''; this.paymentDialog.set(false);
      this.notice.set(result.state === 'PAGADO' ? 'Abono registrado. El apartado quedó pagado y listo para entregar.' : 'Abono registrado.');
      this.drawerNotice.set(cashDrawerNotice(result.drawerStatus));
      await this.refreshDetail(); await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  startCreate(): void { this.error.set(''); this.notice.set(''); this.creating.set(true); }
  async onCreated(created: CreatedLayaway): Promise<void> {
    this.creating.set(false);
    this.notice.set(`Apartado ${created.visibleNumber} creado por ${this.money(created.total)}; saldo ${this.money(created.balance)}${created.change > 0 ? `; cambio ${this.money(created.change)}` : ''}.`);
    this.drawerNotice.set(cashDrawerNotice(created.drawerStatus));
    await this.loadDetail(created.id); await this.reload();
  }
  startCancel(): void { const layaway = this.detail()?.layaway; if (layaway && this.canCancel(layaway)) { this.error.set(''); this.notice.set(''); this.cancelling.set(true); } }
  canCorrectProducts(layaway: Layaway): boolean {
    return (layaway.state === 'ACTIVO' || layaway.state === 'PAGADO') && this.session.has('LAYAWAY.MANAGE') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR');
  }
  startCorrectProducts(): void { const layaway = this.detail()?.layaway; if (layaway && this.canCorrectProducts(layaway) && !this.saving()) { this.error.set(''); this.notice.set(''); this.correctingProducts.set(true); } }
  async onProductsCorrected(result: LayawayProductCorrection): Promise<void> {
    this.correctingProducts.set(false);
    this.notice.set(`Productos corregidos. Total ${this.money(result.previousTotal)} → ${this.money(result.total)}; saldo ${this.money(result.balance)}` +
      (result.refunded > 0 ? `; devuelto ${this.money(result.refunded)}.` : '.'));
    await this.refreshDetail(); await this.reload();
  }
  async onCancelled(result: LayawayCancellation): Promise<void> {
    this.cancelling.set(false);
    this.notice.set(`Apartado ${result.number} cancelado. Devuelto ${this.money(result.refunded)}; retenido ${this.money(result.retained)}.`);
    this.drawerNotice.set(cashDrawerNotice(result.drawerStatus));
    await this.refreshDetail(); await this.reload();
  }

  async deliver(): Promise<void> {
    const layaway = this.detail()?.layaway;
    if (!layaway || this.saving() || !this.canDeliver(layaway)) return;
    if (!await this.confirmation.confirm({ title: 'Entregar apartado', message: `¿Entregar los productos del apartado ${layaway.number} a ${layaway.customerName}? Se descuenta el inventario reservado; no se registra una venta ni un movimiento de Caja.`, confirmText: 'Entregar productos' }, this.owner)) return;
    const current = this.detail()?.layaway;
    if (this.owner.destroyed || this.saving() || !current || current.id !== layaway.id || current.version !== layaway.version || !this.canDeliver(current)) return;
    const fingerprint = JSON.stringify({ id: layaway.id, version: layaway.version });
    if (fingerprint !== this.deliveryFingerprint) { this.deliveryKey = crypto.randomUUID(); this.deliveryFingerprint = fingerprint; }
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try {
      await this.api.deliver(layaway, this.deliveryKey!);
      this.deliveryKey = null; this.deliveryFingerprint = '';
      this.notice.set('Productos entregados.');
      await this.refreshDetail(); await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
