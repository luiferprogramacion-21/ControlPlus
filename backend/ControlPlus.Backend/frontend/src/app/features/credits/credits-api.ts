import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, Page } from '../../core/api';

export interface CreditInstallment { id: string; number: number; amount: number; balance: number; dueOn: string; paidOn: string | null; state: string; }
export interface Credit { id: string; saleId: string; saleNumber: string; customerId: string; customerName: string; documentType: string | null; documentNumber: string | null; phone: string | null; originalAmount: number; adjustedAmount: number; balance: number; grantedOn: string; dueOn: string; paidOn: string | null; state: string; version: number; installments?: CreditInstallment[]; }
export interface CreditPayment { id: string; number: string; methodCode: string; methodName: string; appliedAmount: number; receivedAmount: number | null; change: number; occurredAtUtc: string; state: string; correlationId: string; }
export interface CreditPaymentCancellationReason { id: string; code: string; name: string; displayOrder: number; }
export interface PaymentMethodInput { methodCode: 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI'; appliedAmount: number; receivedAmount: number | null; reference: string | null; }
export interface PaymentResult { creditId: string; balance: number; state: string; version: number; correlationId: string; payments: CreditPayment[]; }
export interface CancelCreditPaymentInput { expectedVersion: number; reasonId: string; observation: string | null; authorization?: AuthorizationBody; }
export interface CancelCreditPaymentResult { creditId: string; balance: number; state: string; version: number; cancelledPaymentIds: string[]; correlationId: string; cancelledAtUtc: string; reversedAmount: number; }
export interface ForgiveCreditResult { creditId: string; balance: number; state: string; version: number; authorizationId: string; correlationId: string; forgivenAtUtc: string; }
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
  paymentCancellationReasons(): Promise<CreditPaymentCancellationReason[]> { return firstValueFrom(this.http.get<CreditPaymentCancellationReason[]>('/api/credits/payment-cancellation-reasons')); }
  cancelPayment(credit: Credit, paymentId: string, input: CancelCreditPaymentInput, key: string): Promise<CancelCreditPaymentResult> {
    return firstValueFrom(this.http.post<CancelCreditPaymentResult>(`/api/credits/${credit.id}/payments/${paymentId}/cancel`, input, { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
  forgive(credit: Credit, reason: string, key: string): Promise<ForgiveCreditResult> {
    return firstValueFrom(this.http.post<ForgiveCreditResult>(`/api/credits/${credit.id}/forgive`, { expectedVersion: credit.version, reason }, { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
}
