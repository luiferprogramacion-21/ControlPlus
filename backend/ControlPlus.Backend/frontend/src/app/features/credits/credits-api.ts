import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../core/api';

export interface Credit { id: string; saleId: string; saleNumber: string; customerId: string; customerName: string; documentType: string | null; documentNumber: string | null; phone: string | null; originalAmount: number; adjustedAmount: number; balance: number; grantedOn: string; dueOn: string; paidOn: string | null; state: string; version: number; }
export interface CreditPayment { id: string; number: string; methodCode: string; methodName: string; appliedAmount: number; receivedAmount: number | null; change: number; occurredAtUtc: string; state: string; correlationId: string; }
export interface PaymentMethodInput { methodCode: 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI'; appliedAmount: number; receivedAmount: number | null; reference: string | null; }
export interface PaymentResult { creditId: string; balance: number; state: string; version: number; correlationId: string; payments: CreditPayment[]; }
export interface CreditFilters { page: number; pageSize: number; state?: string; dueFrom?: string; dueTo?: string; customerId?: string; saleNumber?: string; }

@Injectable({ providedIn: 'root' })
export class CreditsApi {
  private readonly http = inject(HttpClient);
  list(filters: CreditFilters): Promise<Page<Credit>> {
    const params: Record<string, string | number> = { page: filters.page, pageSize: filters.pageSize };
    for (const key of ['state', 'dueFrom', 'dueTo', 'customerId', 'saleNumber'] as const) if (filters[key]) params[key] = filters[key]!;
    return firstValueFrom(this.http.get<Page<Credit>>('/api/credits', { params }));
  }
  get(id: string): Promise<Credit> { return firstValueFrom(this.http.get<Credit>(`/api/credits/${id}`)); }
  payments(id: string, page: number, pageSize: number): Promise<Page<CreditPayment>> { return firstValueFrom(this.http.get<Page<CreditPayment>>(`/api/credits/${id}/payments`, { params: { page, pageSize } })); }
  applyPayment(credit: Credit, payments: PaymentMethodInput[], key: string): Promise<PaymentResult> {
    return firstValueFrom(this.http.post<PaymentResult>(`/api/credits/${credit.id}/payments`, { expectedVersion: credit.version, payments }, { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
}
