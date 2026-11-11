import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page, authorizationContext } from '../../core/api';
export interface PurchaseLineInput { productId: string; quantity: number; unitCost: number; vatPercentage: number | null; }
export interface PurchaseInput { supplierId: string; supplierDocumentNumber: string | null; invoiceDate: string; vatMode: string; lines: PurchaseLineInput[]; }
export interface PurchaseSummary { id: string; supplierId: string; supplierDocumentNumber: string | null; invoiceDate: string; status: string; visibleNumber: string | null; total: number; version: number; }
export interface PurchaseLine extends PurchaseLineInput { id: string; grossSubtotal: number; taxBase: number | null; vatAmount: number | null; total: number; previousAverageCost: number | null; resultingAverageCost: number; }
export interface Purchase extends PurchaseSummary { vatMode: string; grossSubtotal: number; vatTotal: number | null; lines: PurchaseLine[]; orderId: string | null; }
export interface CancellationReason { id: string; code: string; name: string; description: string | null; displayOrder: number; }
@Injectable({ providedIn: 'root' })
export class PurchasesApi {
  private readonly http = inject(HttpClient);
  list(q: { supplierId?: string; status?: string; invoiceDate?: string; supplierDocumentNumber?: string; page?: number; pageSize?: number } = {}): Promise<Page<PurchaseSummary>> {
    const params: Record<string, string | number> = { page: q.page ?? 1, pageSize: q.pageSize ?? 20 };
    if (q.supplierId) params['supplierId'] = q.supplierId;
    if (q.status) params['status'] = q.status;
    if (q.invoiceDate) params['invoiceDate'] = q.invoiceDate;
    if (q.supplierDocumentNumber) params['supplierDocumentNumber'] = q.supplierDocumentNumber;
    return firstValueFrom(this.http.get<Page<PurchaseSummary>>('/api/purchases', { params }));
  }
  get(id: string): Promise<Purchase> { return firstValueFrom(this.http.get<Purchase>(`/api/purchases/${id}`)); }
  create(input: PurchaseInput): Promise<Purchase> { return firstValueFrom(this.http.post<Purchase>('/api/purchases', input)); }
  update(purchase: Purchase, input: PurchaseInput): Promise<Purchase> { return firstValueFrom(this.http.put<Purchase>(`/api/purchases/${purchase.id}`, { ...input, expectedVersion: purchase.version })); }
  confirm(purchase: Purchase, key: string): Promise<Purchase> { return firstValueFrom(this.http.post<Purchase>(`/api/purchases/${purchase.id}/confirm`, { expectedVersion: purchase.version }, { headers: { 'Idempotency-Key': key } })); }
  reasons(): Promise<CancellationReason[]> { return firstValueFrom(this.http.get<CancellationReason[]>('/api/purchases/cancellation-reasons')); }
  cancel(purchase: Purchase, key: string, reasonId: string, observation: string | null, userName: string | null, password: string | null, credentialToken?: string): Promise<Purchase> { const authorized = !!credentialToken || (userName !== null && password !== null); return firstValueFrom(this.http.post<Purchase>(`/api/purchases/${purchase.id}/cancel`, { expectedVersion: purchase.version, reasonId, observation, ...(authorized ? { authorization: credentialToken ? { credentialToken } : { userName, password } } : {}) }, { headers: { 'Idempotency-Key': key }, context: authorized ? authorizationContext() : undefined })); }
}
