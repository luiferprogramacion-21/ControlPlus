import { A11yModule } from '@angular/cdk/a11y';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Customer, CustomerInput, CustomersApi } from './customers-api';
import { CustomerHistory } from './customer-history';
import { Icon } from '../../layout/icon';
@Component({ selector: 'cp-customers', imports: [A11yModule, FormsModule, MatPaginatorModule, Icon, CustomerHistory], templateUrl: './customers.html', styleUrls: ['../directory.scss', './customers.scss'] })
export class Customers implements OnInit {
  private readonly api = inject(CustomersApi);
  readonly session = inject(SessionService);
  readonly result = signal<Page<Customer> | null>(null);
  readonly selected = signal<Customer | null>(null);
  readonly editing = signal(false);
  readonly statusTarget = signal<Customer | null>(null);
  /** Customer whose commercial history is open. */
  readonly historyOf = signal<Customer | null>(null);
  get canReadHistory(): boolean { return this.session.has('CUSTOMERS.HISTORY_READ'); }
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  search = ''; status: 'active' | 'inactive' | 'all' = 'active'; page = 1; pageSize = 20;
  form: CustomerInput = this.empty();
  private requestId = 0;
  get full(): boolean { return this.session.has('CUSTOMERS.MANAGE'); }
  ngOnInit(): void { void this.reload(); }
  private empty(): CustomerInput { return { documentType: null, documentNumber: null, fullName: '', phone: null, address: '', email: null, observations: null, creditLimit: null }; }
  async reload(): Promise<void> {
    const id = ++this.requestId; this.loading.set(true); this.error.set('');
    try { const result = await this.api.list({ search: this.search.trim(), status: this.full ? this.status : 'active', page: this.page, pageSize: this.pageSize }); if (id === this.requestId) this.result.set(result); }
    catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }
  find(): void { this.page = 1; void this.reload(); }
  pageChanged(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  newCustomer(): void { this.selected.set(null); this.form = this.empty(); this.editing.set(true); this.error.set(''); }
  edit(customer: Customer): void { this.selected.set(customer); this.form = { documentType: customer.documentType, documentNumber: customer.documentNumber, fullName: customer.fullName, phone: customer.phone, address: customer.address, email: customer.email, observations: customer.observations, creditLimit: customer.creditLimit ?? null }; this.editing.set(true); this.error.set(''); }
  cancel(): void { this.editing.set(false); this.selected.set(null); }
  private normalized(): CustomerInput { const f = this.form; const value = (x: string | null) => x?.trim() || null; return { documentType: this.full ? value(f.documentType)?.toUpperCase() ?? null : this.selected()?.documentType ?? null, documentNumber: this.full ? value(f.documentNumber)?.toUpperCase() ?? null : this.selected()?.documentNumber ?? null, fullName: f.fullName.trim(), phone: value(f.phone), address: f.address.trim(), email: value(f.email)?.toLowerCase() ?? null, observations: this.full ? value(f.observations) : this.selected()?.observations ?? null, creditLimit: this.full ? limit(f.creditLimit) : this.selected()?.creditLimit ?? null }; }
  validation(): string {
    const f = this.normalized();
    if (!f.fullName || f.fullName.length > 200) return 'Indica un nombre de hasta 200 caracteres.';
    if (!f.address || f.address.length > 300) return 'Indica una dirección de hasta 300 caracteres.';
    if (!!f.documentType !== !!f.documentNumber) return 'Indica tipo y número de documento juntos.';
    if (f.documentNumber && f.documentNumber.length > 50) return 'El documento supera 50 caracteres.';
    if (f.phone && f.phone.length > 30) return 'El teléfono supera 30 caracteres.';
    if (f.email && (f.email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(f.email))) return 'Revisa el correo.';
    if (f.creditLimit != null && (!Number.isInteger(f.creditLimit) || f.creditLimit <= 0)) return 'El cupo de crédito debe ser un valor en pesos enteros mayor que cero, o vacío para no tener límite.';
    return '';
  }
  async save(): Promise<void> {
    if (this.saving()) return; const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try { const selected = this.selected(); if (selected) await this.api.update(selected, this.normalized()); else await this.api.create(this.normalized()); this.cancel(); this.notice.set('Cliente guardado.'); await this.reload(); }
    catch (e) { this.error.set(e instanceof HttpErrorResponse && e.status === 409 ? 'El documento ya existe o el cliente cambió. Consulta la ficha actual antes de guardar.' : errorText(e)); }
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
    catch (e) { this.error.set(e instanceof HttpErrorResponse && e.status === 409 ? 'El cliente cambió. Actualiza la lista antes de intentar otra vez.' : errorText(e)); }
    finally { this.saving.set(false); }
  }
}

/** Empty input means no limit; any other value is sent as a number for the server to validate. */
function limit(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || String(value).trim() === '') return null;
  return Number(value);
}
