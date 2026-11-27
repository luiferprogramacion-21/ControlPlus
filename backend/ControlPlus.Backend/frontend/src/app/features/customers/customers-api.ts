import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../core/api';
import { CreditStageData } from '../../shared/credit-stage';
export interface Customer { id: string; documentType: string | null; documentNumber: string | null; fullName: string; phone: string | null; address: string | null; email: string | null; observations: string | null; isActive: boolean; version: number;
  /** Cupo de crédito en pesos (saldo máximo que puede deber); null = sin límite. */
  creditLimit?: number | null; }
export interface CustomerHistoryItem { id: string; type: 'SALE' | 'CREDIT' | 'CREDIT_PAYMENT' | 'LAYAWAY' | 'LAYAWAY_PAYMENT' | string; parentId: string | null; number: string; occurredAt: string; status: string; amount: number; outstandingAmount: number | null; }
export type CustomerInput = Pick<Customer, 'documentType' | 'documentNumber' | 'fullName' | 'phone' | 'address' | 'email' | 'observations' | 'creditLimit'>;
/** Grupos de la lista de Clientes según el crédito (pruebas del usuario del 26/11/2026). */
export type CustomerCreditFilter = '' | 'DEBEN' | 'MOROSOS' | 'POR_VENCER' | 'SIN_DEUDA';
/** Resumen de arriba de Clientes: cuántos deben y cuánto, morosos (lo vencido) y los que vencen pronto. */
export interface CustomerCreditSummary { debtors: number; debtTotal: number; overdue: number; overdueTotal: number; dueSoon: number; dueSoonTotal: number; }
@Injectable({ providedIn: 'root' })
export class CustomersApi {
  private readonly http = inject(HttpClient);
  /** Auditoría C.14: etapa del crédito (al día, por vencer o moroso) de los clientes de una página. */
  creditStages(customerIds: string[]): Promise<(CreditStageData & { customerId: string })[]> {
    if (customerIds.length === 0) return Promise.resolve([]);
    return firstValueFrom(this.http.get<(CreditStageData & { customerId: string })[]>('/api/sales/customer-credit-stages', { params: { customerIds } }));
  }
  list(params: { search?: string; status?: 'active' | 'inactive' | 'all'; page?: number; pageSize?: number; credit?: CustomerCreditFilter } = {}): Promise<Page<Customer>> {
    const query: Record<string, string | number> = { search: params.search ?? '', status: params.status ?? 'active', page: params.page ?? 1, pageSize: params.pageSize ?? 20 };
    if (params.credit) query['credit'] = params.credit;
    return firstValueFrom(this.http.get<Page<Customer>>('/api/customers', { params: query }));
  }
  creditSummary(): Promise<CustomerCreditSummary> { return firstValueFrom(this.http.get<CustomerCreditSummary>('/api/sales/customer-credit-summary')); }
  get(id: string): Promise<Customer> { return firstValueFrom(this.http.get<Customer>(`/api/customers/${id}`)); }
  history(id: string, page = 1, pageSize = 20): Promise<Page<CustomerHistoryItem>> { return firstValueFrom(this.http.get<Page<CustomerHistoryItem>>(`/api/customers/${id}/history`, { params: { page, pageSize } })); }
  create(input: CustomerInput): Promise<Customer> { return firstValueFrom(this.http.post<Customer>('/api/customers', input)); }
  update(customer: Customer, input: CustomerInput): Promise<Customer> { return firstValueFrom(this.http.put<Customer>(`/api/customers/${customer.id}`, { ...input, expectedVersion: customer.version })); }
  status(customer: Customer): Promise<Customer> { return firstValueFrom(this.http.put<Customer>(`/api/customers/${customer.id}/status`, { isActive: !customer.isActive, expectedVersion: customer.version })); }
}
