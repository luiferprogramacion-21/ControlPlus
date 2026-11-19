import { A11yModule } from '@angular/cdk/a11y';
import { Component, inject, input, OnDestroy, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Customer, CustomersApi } from './customers-api';
import { toastSignal } from '../../core/toast';

const SEARCH_DELAY_MS = 250;
const DOCUMENT_TYPES = ['CC', 'CE', 'NIT', 'PASAPORTE', 'PPT', 'OTRO'];

/** Window to find a customer (or register one without leaving the sale), as in the prototype. */
@Component({ selector: 'cp-customer-picker', imports: [FormsModule, A11yModule], templateUrl: './customer-picker.html', styles: ['.customer-doc-row { display: grid; grid-template-columns: 150px 1fr; gap: 8px; }'] })
export class CustomerPicker implements OnInit, OnDestroy {
  private readonly api = inject(CustomersApi);
  readonly session = inject(SessionService);
  /** A credit sale or a layaway needs a customer: «consumidor final» is not offered. */
  readonly required = input(false);
  readonly title = input('Seleccionar cliente');
  readonly chosen = output<Customer | null>();
  readonly closed = output<void>();
  readonly results = signal<Customer[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly creating = signal(false);
  readonly error = toastSignal('error');
  readonly documentTypes = DOCUMENT_TYPES;
  search = '';
  form = { documentType: '', documentNumber: '', fullName: '', phone: '', address: '' };
  private timer: ReturnType<typeof setTimeout> | undefined;
  private seq = 0;

  get canCreate(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }
  /** Documents are written only with the full permission; the basic one registers name, phone and address. */
  get canWriteDocument(): boolean { return this.session.has('CUSTOMERS.MANAGE') || this.session.has('CUSTOMERS.BASIC_MANAGE'); }

  ngOnInit(): void { void this.load(''); }
  ngOnDestroy(): void { clearTimeout(this.timer); }

  /** For a credit the customer needs a complete document or a phone. */
  eligible(customer: Customer): boolean { return !this.required() || !!(customer.documentType && customer.documentNumber) || !!customer.phone; }

  onSearch(value: string): void {
    this.search = value; clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.load(value.trim()), SEARCH_DELAY_MS);
  }
  private async load(term: string): Promise<void> {
    const seq = ++this.seq; this.loading.set(true); this.error.set('');
    try {
      const page = await this.api.list({ search: term, status: 'active', page: 1, pageSize: 20 });
      if (seq === this.seq) this.results.set(page.items);
    } catch (e) { if (seq === this.seq) this.error.set(errorText(e)); }
    finally { if (seq === this.seq) this.loading.set(false); }
  }

  choose(customer: Customer): void { if (this.eligible(customer)) this.chosen.emit(customer); }
  consumerFinal(): void { this.chosen.emit(null); }
  /** Opens the registration form, reusing what was typed: digits become the phone, text becomes the name. */
  startCreate(): void {
    const typed = this.search.trim();
    const onlyDigits = /^[\d\s+-]+$/.test(typed);
    this.error.set('');
    this.form = { documentType: '', documentNumber: '', fullName: typed && !onlyDigits ? typed : '', phone: onlyDigits ? typed : '', address: '' };
    this.creating.set(true);
  }

  validation(): string {
    const f = this.form; const name = f.fullName.trim(); const address = f.address.trim();
    if (!name || name.length > 200) return 'Indica un nombre de hasta 200 caracteres.';
    if (!address || address.length > 300) return 'Indica una dirección de hasta 300 caracteres.';
    if (this.canWriteDocument && !!f.documentType !== !!f.documentNumber.trim()) return 'Indica tipo y número de documento juntos.';
    if (f.documentNumber.trim().length > 50) return 'El documento supera 50 caracteres.';
    if (f.phone.trim().length > 30) return 'El teléfono supera 30 caracteres.';
    if (this.required() && !f.phone.trim() && !(f.documentType && f.documentNumber.trim())) return 'Para un crédito o apartado indica el teléfono o el documento completo.';
    return '';
  }
  async create(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    const f = this.form; this.saving.set(true); this.error.set('');
    try {
      const created = await this.api.create({
        documentType: this.canWriteDocument && f.documentType ? f.documentType : null,
        documentNumber: this.canWriteDocument && f.documentType ? f.documentNumber.trim().toUpperCase() : null,
        fullName: f.fullName.trim(), phone: f.phone.trim() || null, address: f.address.trim(), email: null, observations: null,
      });
      this.chosen.emit(created);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.saving.set(false); }
  }
}
