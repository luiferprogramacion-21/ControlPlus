import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { Customer, CustomersApi } from '../customers/customers-api';
import { CreatedLayaway, Layaway, LayawayCancellation, LayawayDetail, LayawayPaymentInput, LayawaysApi } from './layaways-api';
import { LayawayCancel } from './layaway-cancel';
import { LayawayCreate } from './layaway-create';
import { appliedTotal, cashChange, emptyDraft, PaymentDraft, PaymentMethods, toPayments, validateDrafts } from './payment-methods';
import { toastSignal } from '../../core/toast';

const STATE_LABELS: Record<string, string> = { ACTIVO: 'Activo', PAGADO: 'Pagado', ENTREGADO: 'Entregado', ANULADO: 'Anulado' };

@Component({
  selector: 'cp-layaways',
  imports: [DatePipe, FormsModule, MatPaginatorModule, Icon, PaymentMethods, LayawayCreate, LayawayCancel],
  templateUrl: './layaways.html',
  styleUrls: ['../directory.scss', './layaways.scss'],
})
export class Layaways implements OnInit {
  private readonly api = inject(LayawaysApi);
  private readonly customersApi = inject(CustomersApi);
  readonly session = inject(SessionService);
  readonly result = signal<Page<Layaway> | null>(null);
  readonly detail = signal<LayawayDetail | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  readonly paymentDialog = signal(false);
  readonly creating = signal(false);
  readonly cancelling = signal(false);
  readonly customerMatches = signal<Customer[]>([]);
  readonly customerLabel = signal('');
  state = ''; overdue: '' | 'true' | 'false' = ''; dueFrom = ''; dueTo = ''; customerSearch = ''; customerId = '';
  page = 1; pageSize = 20;
  drafts: PaymentDraft[] = [emptyDraft()];
  private requestId = 0;
  private paymentKey: string | null = null; private paymentFingerprint = '';
  private deliveryKey: string | null = null; private deliveryFingerprint = '';

  get canFindCustomers(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }
  get appliedTotal(): number { return appliedTotal(this.drafts); }
  get change(): number { return cashChange(this.drafts); }

  ngOnInit(): void { void this.reload(); }
  money(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value); }
  label(state: string): string { return STATE_LABELS[state] ?? state; }
  paid(layaway: Layaway): number { return layaway.total - layaway.balance; }
  canPay(layaway: Layaway): boolean { return layaway.state === 'ACTIVO' && layaway.balance > 0; }
  /** Cancelling is an Administrator decision and needs both effective permissions. */
  canCancel(layaway: Layaway): boolean {
    return (layaway.state === 'ACTIVO' || layaway.state === 'PAGADO') && this.session.has('LAYAWAY.CANCEL') && this.session.has('LAYAWAY.REFUND_DECIDE') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR');
  }
  canDeliver(layaway: Layaway): boolean { return layaway.state === 'PAGADO' && layaway.balance === 0; }

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
      await this.refreshDetail(); await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }

  startCreate(): void { this.error.set(''); this.notice.set(''); this.creating.set(true); }
  async onCreated(created: CreatedLayaway): Promise<void> {
    this.creating.set(false);
    this.notice.set(`Apartado ${created.visibleNumber} creado por ${this.money(created.total)}; saldo ${this.money(created.balance)}${created.change > 0 ? `; cambio ${this.money(created.change)}` : ''}.`);
    await this.loadDetail(created.id); await this.reload();
  }
  startCancel(): void { const layaway = this.detail()?.layaway; if (layaway && this.canCancel(layaway)) { this.error.set(''); this.notice.set(''); this.cancelling.set(true); } }
  async onCancelled(result: LayawayCancellation): Promise<void> {
    this.cancelling.set(false);
    this.notice.set(`Apartado ${result.number} cancelado. Devuelto ${this.money(result.refunded)}; retenido ${this.money(result.retained)}.`);
    await this.refreshDetail(); await this.reload();
  }

  async deliver(): Promise<void> {
    const layaway = this.detail()?.layaway;
    if (!layaway || this.saving() || !this.canDeliver(layaway)) return;
    if (!confirm(`Entregar los productos del apartado ${layaway.number} a ${layaway.customerName}? Se descuenta el inventario reservado; no se registra una venta ni un movimiento de Caja.`)) return;
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
