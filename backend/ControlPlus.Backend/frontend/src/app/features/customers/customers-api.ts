import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../core/api';
export interface Customer { id: string; documentType: string | null; documentNumber: string | null; fullName: string; phone: string | null; address: string; email: string | null; observations: string | null; isActive: boolean; version: number;
  /** Cupo de crédito en pesos (saldo máximo que puede deber); null = sin límite. */
  creditLimit?: number | null; }
export interface CustomerHistoryItem { id: string; type: 'SALE' | 'CREDIT' | 'CREDIT_PAYMENT' | 'LAYAWAY' | 'LAYAWAY_PAYMENT' | string; parentId: string | null; number: string; occurredAt: string; status: string; amount: number; outstandingAmount: number | null; }
export type CustomerInput = Pick<Customer, 'documentType' | 'documentNumber' | 'fullName' | 'phone' | 'address' | 'email' | 'observations' | 'creditLimit'>;
@Injectable({ providedIn: 'root' })
export class CustomersApi {
  private readonly http = inject(HttpClient);
  list(params: { search?: string; status?: 'active' | 'inactive' | 'all'; page?: number; pageSize?: number } = {}): Promise<Page<Customer>> {
    return firstValueFrom(this.http.get<Page<Customer>>('/api/customers', { params: { search: params.search ?? '', status: params.status ?? 'active', page: params.page ?? 1, pageSize: params.pageSize ?? 20 } }));
  }
  get(id: string): Promise<Customer> { return firstValueFrom(this.http.get<Customer>(`/api/customers/${id}`)); }
  history(id: string, page = 1, pageSize = 20): Promise<Page<CustomerHistoryItem>> { return firstValueFrom(this.http.get<Page<CustomerHistoryItem>>(`/api/customers/${id}/history`, { params: { page, pageSize } })); }
  create(input: CustomerInput): Promise<Customer> { return firstValueFrom(this.http.post<Customer>('/api/customers', input)); }
  update(customer: Customer, input: CustomerInput): Promise<Customer> { return firstValueFrom(this.http.put<Customer>(`/api/customers/${customer.id}`, { ...input, expectedVersion: customer.version })); }
  status(customer: Customer): Promise<Customer> { return firstValueFrom(this.http.put<Customer>(`/api/customers/${customer.id}/status`, { isActive: !customer.isActive, expectedVersion: customer.version })); }
}
