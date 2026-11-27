import { applyFormat, documentRule, InputFormat } from '../../shared/input-format';
import { groupedNumber, NumberInput } from '../../shared/number-input';
import { A11yModule } from '@angular/cdk/a11y';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText, reasonOr } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Customer, CustomerCreditFilter, CustomerCreditSummary, CustomerInput, CustomersApi } from './customers-api';
import { Credit, CreditsApi } from '../credits/credits-api';
import { CustomerHistory } from './customer-history';
import { Icon } from '../../layout/icon';
import { toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';
import { HelpTip } from '../../shared/help-tip';
import { CreditStageData, creditStageView } from '../../shared/credit-stage';
import { isEmailAddress } from '../../shared/email-address';
@Component({ selector: 'cp-customers', imports: [WindowSheet, HelpTip, NumberInput, InputFormat, A11yModule, FormsModule, MatPaginatorModule, Icon, CustomerHistory], templateUrl: './customers.html', styleUrls: ['../directory.scss', './customers.scss'] })
export class Customers implements OnInit {
  private readonly api = inject(CustomersApi);
  private readonly creditsApi = inject(CreditsApi);
  private readonly router = inject(Router);
  readonly session = inject(SessionService);
  /** Pruebas del usuario del 26/11/2026: resumen de arriba, grupos por crédito y ficha con pestañas. */
  readonly summary = signal<CustomerCreditSummary | null>(null);
  readonly chips: { value: CustomerCreditFilter; label: string }[] = [
    { value: '', label: 'Todos' }, { value: 'DEBEN', label: 'Deben' }, { value: 'MOROSOS', label: 'Morosos' },
    { value: 'POR_VENCER', label: 'Por vencer' }, { value: 'SIN_DEUDA', label: 'Sin deuda' },
  ];
  creditFilter: CustomerCreditFilter = '';
  readonly fichaTab = signal<'datos' | 'creditos' | 'historial'>('datos');
  readonly customerCredits = signal<Credit[] | null>(null);
  readonly number = groupedNumber;
  money(value: number): string { return `$ ${groupedNumber(value)}`; }
  /** Quien cobra créditos ve el botón «Cobrar abono» y la pestaña Créditos. */
  get canCollect(): boolean { return this.session.has('CREDIT.MANAGE'); }
  readonly result = signal<Page<Customer> | null>(null);
  readonly selected = signal<Customer | null>(null);
  readonly editing = signal(false);
  readonly statusTarget = signal<Customer | null>(null);
  /** Customer whose commercial history is open. */
  readonly historyOf = signal<Customer | null>(null);
  get canReadHistory(): boolean { return this.session.has('CUSTOMERS.HISTORY_READ'); }
  readonly loading = signal(false);
  readonly saving = signal(false);
  /** Auditoría C.14: etapa del crédito de cada cliente de la página, con su color. */
  readonly stages = signal<Record<string, CreditStageData>>({});
  readonly creditStageView = creditStageView;
  /** Pruebas del usuario del 26/11/2026: lo que debe cada cliente en total, con su saldo de créditos pendientes. */
  debt(customerId: string): string {
    const balance = this.stages()[customerId]?.outstandingBalance ?? 0;
    return balance > 0 ? `$ ${groupedNumber(balance)}` : '—';
  }
  hasDebt(customerId: string): boolean { return (this.stages()[customerId]?.outstandingBalance ?? 0) > 0; }
  stageOf(customerId: string): string { return this.stages()[customerId]?.stage ?? 'SIN_DEUDA'; }
  /** Color del círculo con las iniciales: rojo moroso, naranja por vencer, verde al día, gris sin deuda. */
  toneOf(customerId: string): string { return creditStageView(this.stages()[customerId])?.tone ?? 'neutral'; }
  /** «Moroso desde 10/11», «Por vencer · 27/11», «Al día · vence 15/12» o «Sin deuda». */
  stageText(customerId: string): string {
    const data = this.stages()[customerId];
    const view = creditStageView(data);
    if (!data || !view) return '';
    if (data.stage === 'VENCIDO' && data.oldestOverdueOn) return `Moroso desde ${shortDate(data.oldestOverdueOn)}`;
    if (data.stage === 'POR_VENCER' && data.nextDueOn) return `Por vencer · ${shortDate(data.nextDueOn)}`;
    if (data.stage === 'AL_DIA' && data.nextDueOn) return `Al día · vence ${shortDate(data.nextDueOn)}`;
    return view.label;
  }
  initials(name: string): string {
    const words = name.trim().split(/\s+/).filter(Boolean);
    return ((words[0]?.[0] ?? '') + (words.length > 1 ? words[words.length - 1][0] : words[0]?.[1] ?? '')).toUpperCase();
  }
  /** Documento, teléfono y dirección en una sola línea. */
  contactLine(customer: Customer): string {
    const document = customer.documentType && customer.documentNumber ? `${customer.documentType} ${customer.documentNumber}` : 'Sin documento';
    return [document, customer.phone || 'Sin teléfono', customer.address || 'Sin dirección'].join(' · ');
  }
  shortDate(value: string): string { return shortDate(value); }
  creditStateLabel(state: string): string {
    return ({ PENDIENTE: 'Pendiente', VENCIDO: 'Vencido', PAGADO: 'Pagado', ANULADO: 'Anulado' } as Record<string, string>)[state] ?? state;
  }
  setFilter(filter: CustomerCreditFilter): void { this.creditFilter = filter; this.find(); }
  /** «Cobrar abono» abre Créditos con este cliente; desde la ficha, con el crédito elegido listo para abonar. */
  collect(customer: Customer, creditId?: string): void {
    void this.router.navigate(['/operacion/creditos'], { queryParams: { cliente: customer.id, nombre: customer.fullName, ...(creditId ? { abonar: creditId } : {}) } });
  }
  openFicha(customer: Customer, tab: 'datos' | 'creditos' | 'historial' = 'datos'): void {
    this.edit(customer);
    this.setTab(tab === 'historial' && !this.canReadHistory ? 'datos' : tab === 'creditos' && !this.canCollect ? 'datos' : tab);
  }
  setTab(tab: 'datos' | 'creditos' | 'historial'): void {
    this.fichaTab.set(tab);
    const customer = this.selected();
    if (tab === 'creditos' && customer) void this.loadCustomerCredits(customer.id);
  }
  private async loadCustomerCredits(customerId: string): Promise<void> {
    this.customerCredits.set(null);
    try {
      const page = await this.creditsApi.list({ page: 1, pageSize: 50, customerId });
      if (this.selected()?.id === customerId) this.customerCredits.set([...page.items].sort((a, b) => Number(b.balance > 0) - Number(a.balance > 0)));
    } catch (e) { if (this.selected()?.id === customerId) { this.customerCredits.set([]); this.error.set(errorText(e)); } }
  }
  get canSeeCredit(): boolean { return this.session.has('CREDIT.REQUEST'); }
  readonly error = toastSignal('error');
  readonly notice = toastSignal('success');
  search = ''; status: 'active' | 'inactive' | 'all' = 'active'; page = 1; pageSize = 20;
  form: CustomerInput = this.empty();
  private requestId = 0;
  get full(): boolean { return this.session.has('CUSTOMERS.MANAGE'); }
  ngOnInit(): void { void this.reload(); }
  private empty(): CustomerInput { return { documentType: null, documentNumber: null, fullName: '', phone: null, address: '', email: null, observations: null, creditLimit: null }; }
  async reload(): Promise<void> {
    const id = ++this.requestId; this.loading.set(true); this.error.set('');
    try {
      const result = await this.api.list({ search: this.search.trim(), status: this.full ? this.status : 'active', page: this.page, pageSize: this.pageSize,
        credit: this.canSeeCredit ? this.creditFilter : '' });
      if (id !== this.requestId) return;
      this.result.set(result);
      void this.loadStages(id, result.items.map(customer => customer.id));
      void this.loadSummary(id);
    }
    catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }
  /** El resumen de arriba; si falla, la lista se ve igual sin él. */
  private async loadSummary(id: number): Promise<void> {
    if (!this.canSeeCredit) { this.summary.set(null); return; }
    try { const summary = await this.api.creditSummary(); if (id === this.requestId) this.summary.set(summary); }
    catch { if (id === this.requestId) this.summary.set(null); }
  }
  /** Si falla, la lista se ve igual, solo sin la etapa del crédito. */
  private async loadStages(id: number, customerIds: string[]): Promise<void> {
    if (!this.canSeeCredit) { this.stages.set({}); return; }
    try {
      const stages = await this.api.creditStages(customerIds);
      if (id === this.requestId) this.stages.set(Object.fromEntries(stages.map(stage => [stage.customerId, stage])));
    } catch { if (id === this.requestId) this.stages.set({}); }
  }
  find(): void { this.page = 1; void this.reload(); }
  pageChanged(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  newCustomer(): void { this.fichaTab.set('datos'); this.selected.set(null); this.form = this.empty(); this.editing.set(true); this.error.set(''); }
  edit(customer: Customer): void { this.fichaTab.set('datos'); this.selected.set(customer); this.form = { documentType: customer.documentType, documentNumber: customer.documentNumber, fullName: customer.fullName, phone: customer.phone, address: customer.address, email: customer.email, observations: customer.observations, creditLimit: customer.creditLimit ?? null }; this.editing.set(true); this.error.set(''); }
  cancel(): void { this.editing.set(false); this.selected.set(null); this.customerCredits.set(null); this.fichaTab.set('datos'); }
  private normalized(): CustomerInput { const f = this.form; const value = (x: string | null) => x?.trim() || null; return { documentType: value(f.documentType)?.toUpperCase() ?? null, documentNumber: value(f.documentNumber)?.toUpperCase() ?? null, fullName: f.fullName.trim(), phone: value(f.phone), address: value(f.address), email: value(f.email)?.toLowerCase() ?? null, observations: this.full ? value(f.observations) : this.selected()?.observations ?? null, creditLimit: this.full ? limit(f.creditLimit) : this.selected()?.creditLimit ?? null }; }
  validation(): string {
    const f = this.normalized();
    if (!f.fullName || f.fullName.length > 200) return 'Indica un nombre de hasta 200 caracteres.';
    // Decisión del 21 de noviembre de 2026: todo cliente lleva nombre, documento, teléfono y dirección.
    if (!f.documentType || !f.documentNumber) return 'Indica el tipo y el número de documento.';
    if (applyFormat(f.documentNumber, documentRule(f.documentType)) !== f.documentNumber) return 'Revisa el número de documento: tiene caracteres que no van o es muy largo.';
    if (!/^\d{7,10}$/.test(f.phone ?? '')) return 'El teléfono debe tener entre 7 y 10 números.';
    if (!f.address || f.address.length > 300) return 'Indica la dirección (hasta 300 caracteres).';
    if (f.email && !isEmailAddress(f.email)) return 'Revisa el correo.';
    if (f.creditLimit != null && (!Number.isInteger(f.creditLimit) || f.creditLimit <= 0)) return 'El cupo de crédito debe ser un valor en pesos enteros mayor que cero, o vacío para no tener límite.';
    return '';
  }
  async save(): Promise<void> {
    if (this.saving()) return; const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try { const selected = this.selected(); if (selected) await this.api.update(selected, this.normalized()); else await this.api.create(this.normalized()); this.cancel(); this.notice.set('Cliente guardado.'); await this.reload(); }
    catch (e) { this.error.set(e instanceof HttpErrorResponse && e.status === 409 ? reasonOr(e, 'El documento ya existe o el cliente cambió. Consulta la ficha actual antes de guardar.') : errorText(e)); }
    finally { this.saving.set(false); }
  }
  async refreshSelected(): Promise<void> { const selected = this.selected(); if (!selected) return; try { this.edit(await this.api.get(selected.id)); await this.reload(); } catch (e) { this.error.set(errorText(e)); } }
  requestStatusChange(customer: Customer): void {
    if (!this.full || this.saving()) return;
    this.error.set(''); this.notice.set(''); this.statusTarget.set(customer);
  }
  toggle(customer: Customer): void { this.requestStatusChange(customer); }
  cancelStatusChange(): void { if (!this.saving()) { this.statusTarget.set(null); this.error.set(''); } }
  async confirmStatusChange(): Promise<void> {
    const customer = this.statusTarget();
    if (!customer || !this.full || this.saving()) return;
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try { await this.api.status(customer); this.statusTarget.set(null); this.notice.set(customer.isActive ? 'Cliente inactivado.' : 'Cliente activado.'); await this.reload(); }
    catch (e) { this.error.set(e instanceof HttpErrorResponse && e.status === 409 ? reasonOr(e, 'El cliente cambió. Actualiza la lista antes de intentar otra vez.') : errorText(e)); }
    finally { this.saving.set(false); }
  }
}

/** Empty input means no limit; any other value is sent as a number for the server to validate. */
function limit(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || String(value).trim() === '') return null;
  return Number(value);
}

/** «2026-11-27» → «27/11». */
function shortDate(value: string): string {
  const [, month, day] = value.slice(0, 10).split('-');
  return day && month ? `${day}/${month}` : value;
}
