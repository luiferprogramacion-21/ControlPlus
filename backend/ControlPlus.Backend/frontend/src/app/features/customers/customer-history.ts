import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { Customer, CustomerHistoryItem, CustomersApi } from './customers-api';

const TYPE_LABELS: Record<string, string> = {
  SALE: 'Venta', CREDIT: 'Crédito', CREDIT_PAYMENT: 'Abono de crédito', LAYAWAY: 'Apartado', LAYAWAY_PAYMENT: 'Abono de apartado',
};
const STATUS_LABELS: Record<string, string> = {
  CONFIRMADA: 'Confirmada', ANULADA: 'Anulada', PENDIENTE: 'Pendiente', PAGADO: 'Pagado', VENCIDO: 'Vencido', ANULADO: 'Anulado',
  ACTIVO: 'Activo', ENTREGADO: 'Entregado', APLICADO: 'Aplicado',
};

/** Commercial history of one customer: sales, credits, layaways and their payments, newest first. */
@Component({
  selector: 'cp-customer-history',
  imports: [CurrencyPipe, DatePipe, MatPaginatorModule],
  templateUrl: './customer-history.html',
  styles: ['.customer-history-sheet { width: min(980px, 100%); max-height: 92vh; overflow-y: auto; } .is-payment td { color: var(--cp-text-secondary); font-size: 13px; }'],
})
export class CustomerHistory implements OnInit {
  private readonly api = inject(CustomersApi);
  readonly customer = input.required<Customer>();
  readonly closed = output<void>();
  readonly result = signal<Page<CustomerHistoryItem> | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  page = 1; pageSize = 20;
  private requestId = 0;

  ngOnInit(): void { void this.load(); }
  typeLabel(type: string): string { return TYPE_LABELS[type] ?? type; }
  statusLabel(status: string): string { return STATUS_LABELS[status] ?? status; }
  /** Payments are shown indented under their document type; the amount is what was applied. */
  isPayment(type: string): boolean { return type === 'CREDIT_PAYMENT' || type === 'LAYAWAY_PAYMENT'; }

  async load(): Promise<void> {
    const id = ++this.requestId; this.loading.set(true); this.error.set('');
    try {
      const result = await this.api.history(this.customer().id, this.page, this.pageSize);
      if (id === this.requestId) this.result.set(result);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }
  pageChanged(event: PageEvent): void { this.page = event.pageIndex + 1; this.pageSize = event.pageSize; void this.load(); }
}
