import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { authorizationContext } from '../../core/api';
import { Page } from '../../core/api';
import { Customer, CustomersApi } from '../customers/customers-api';

export interface SaleDiscount { type: 'PORCENTAJE' | 'VALOR_FIJO'; value: number; }
export interface SaleLineInput { productId: string; quantity: number; priceType: 'MINORISTA' | 'MAYORISTA'; discount: SaleDiscount | null; }
export interface SalePaymentInput { methodCode: 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI'; appliedAmount: number; receivedAmount: number | null; reference: string | null; }
export interface SaleInput { customerId: string | null; lines: SaleLineInput[]; generalDiscount: SaleDiscount | null; discountReason: string | null; payments: SalePaymentInput[]; saleType: 'CONTADO' | 'CREDITO'; dueDate: string | null; authorization: { userName: string; password: string } | null; }
export interface Sale { id: string; visibleNumber: string; total: number; change: number; state: string; payments: SalePaymentInput[]; creditId: string | null; }

@Injectable({ providedIn: 'root' })
export class SalesApi {
  private readonly http = inject(HttpClient);
  private readonly customersApi = inject(CustomersApi);
  confirm(input: SaleInput, key: string): Promise<Sale> {
    return firstValueFrom(this.http.post<Sale>('/api/sales', input, { headers: new HttpHeaders({ 'Idempotency-Key': key }), context: input.authorization ? authorizationContext() : undefined }));
  }
  get(id: string): Promise<Sale> { return firstValueFrom(this.http.get<Sale>(`/api/sales/${id}`)); }
  customers(search: string): Promise<Page<Customer>> { return this.customersApi.list({ search, status: 'active', page: 1, pageSize: 20 }); }
}
