import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { CustomersApi, Customer } from '../customers/customers-api';
import { Icon } from '../../layout/icon';
import { Credit, CreditPayment, CreditsApi, PaymentMethodInput } from './credits-api';

type Method = PaymentMethodInput['methodCode'];
type Draft = { methodCode: Method; appliedAmount: number; receivedAmount: number | null; reference: string; };
const LIMIT = Number.MAX_SAFE_INTEGER;
@Component({ selector: 'cp-credits', imports: [FormsModule, MatPaginatorModule, Icon, RouterLink], templateUrl: './credits.html', styleUrls: ['../directory.scss', './credits.scss'] })
export class Credits implements OnInit {
  private readonly api = inject(CreditsApi);
  private readonly customersApi = inject(CustomersApi);
  readonly session = inject(SessionService);
  readonly result = signal<Page<Credit> | null>(null);
  readonly selected = signal<Credit | null>(null);
  readonly history = signal<Page<CreditPayment> | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly dialog = signal(false);
  readonly customerMatches = signal<Customer[]>([]);
  readonly customerLabel = signal('');
  state = ''; saleNumber = ''; dueFrom = ''; dueTo = ''; customerSearch = ''; customerId = '';
  page = 1; pageSize = 20; historyPage = 1; historyPageSize = 20;
  drafts: Draft[] = [this.emptyDraft()];
  private requestId = 0;
  private pendingKey: string | null = null;
  private pendingFingerprint = '';
  get canReadHistory(): boolean { return this.session.has('CUSTOMERS.HISTORY_READ'); }
  get canFindCustomers(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }
  get appliedTotal(): number { return this.drafts.reduce((sum, draft) => sum + Number(draft.appliedAmount || 0), 0); }
  get change(): number { const cash = this.drafts.find(d => d.methodCode === 'EFECTIVO'); return cash ? Math.max(0, Number(cash.receivedAmount || 0) - Number(cash.appliedAmount || 0)) : 0; }
  ngOnInit(): void { void this.reload(); }
  money(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value); }
  paid(credit: Credit): number { return credit.adjustedAmount - credit.balance; }
  label(state: string): string { return ({ PENDIENTE: 'Pendiente', VENCIDO: 'Vencido', PAGADO: 'Pagado', ANULADO: 'Anulado' } as Record<string, string>)[state] ?? state; }
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
    this.selected.set(credit); this.history.set(null); this.historyPage = 1; this.error.set(''); this.notice.set('');
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
  close(): void { if (this.saving()) return; this.selected.set(null); this.dialog.set(false); this.history.set(null); this.pendingKey = null; }
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
      await this.api.applyPayment(credit, payments, this.pendingKey!);
      this.pendingKey = null; this.pendingFingerprint = ''; this.dialog.set(false); this.notice.set('Abono registrado.');
      await this.refreshSelected(); await this.reload();
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
