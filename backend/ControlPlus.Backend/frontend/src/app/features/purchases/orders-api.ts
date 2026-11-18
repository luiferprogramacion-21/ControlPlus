import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, Page, authorizationContext } from '../../core/api';
import { Purchase } from './purchases-api';
export interface OrderLine { id: string; productId: string; productCode: string; productName: string; requestedQuantity: number; receivedQuantity: number; pendingQuantity: number; excessQuantity: number; suggestedQuantity: number; estimatedUnitCost: number | null; observation: string | null; }
export interface OrderLineSummary { productCode: string; productName: string; requestedQuantity: number; receivedQuantity: number; }
export interface OrderSummary { id: string; supplierId: string; status: string; visibleNumber: string; createdAt: string; version: number; lines: OrderLineSummary[]; }
export interface Order extends OrderSummary { lines: OrderLine[]; sentAt: string | null; closedAt: string | null; canceledAt?: string | null; closeReasonId?: string | null; cancelReasonId?: string | null; }
export interface OrderReason { id: string; code: string; name: string; displayOrder: number; }
export interface OrderTransitionInput { reasonId: string; observation: string | null; authorization?: AuthorizationBody; }
export interface OrderDraftLine { productId: string; requestedQuantity: number; estimatedUnitCost?: number | null; observation?: string | null; }
export interface OrderDraftInput { supplierId: string; lines: OrderDraftLine[]; }
export interface ReceiptLine { orderLineId: string; productId: string; quantity: number; unitCost: number; vatPercentage: number | null; newRetailPrice: number | null; }
export interface ReceiptInput { expectedOrderVersion: number; supplierDocumentNumber: string | null; invoiceDate: string; vatMode: string; lines: ReceiptLine[]; authorization?: AuthorizationBody; }
@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);
  list(q: { supplierId?: string; status?: string; page?: number; pageSize?: number } = {}): Promise<Page<OrderSummary>> { const params: Record<string, string | number> = { page: q.page ?? 1, pageSize: q.pageSize ?? 20 }; if (q.supplierId) params['supplierId'] = q.supplierId; if (q.status) params['status'] = q.status; return firstValueFrom(this.http.get<Page<OrderSummary>>('/api/purchase-orders', { params })); }
  get(id: string): Promise<Order> { return firstValueFrom(this.http.get<Order>(`/api/purchase-orders/${id}`)); }
  create(input: OrderDraftInput): Promise<Order> { return firstValueFrom(this.http.post<Order>('/api/purchase-orders', input)); }
  update(order: Order, lines: OrderDraftLine[]): Promise<Order> { return firstValueFrom(this.http.put<Order>(`/api/purchase-orders/${order.id}`, { expectedVersion: order.version, lines })); }
  send(order: Order): Promise<Order> { return firstValueFrom(this.http.post<Order>(`/api/purchase-orders/${order.id}/send`, { expectedVersion: order.version })); }
  receive(order: Order, key: string, input: Omit<ReceiptInput, 'expectedOrderVersion'>): Promise<{ purchase: Purchase; orderId: string; orderStatus: string; orderVersion: number }> { return firstValueFrom(this.http.post<{ purchase: Purchase; orderId: string; orderStatus: string; orderVersion: number }>(`/api/purchase-orders/${order.id}/receipts`, { ...input, expectedOrderVersion: order.version }, { headers: { 'Idempotency-Key': key } })); }
  closeReasons(): Promise<OrderReason[]> { return firstValueFrom(this.http.get<OrderReason[]>('/api/purchase-orders/reasons/close')); }
  cancelReasons(): Promise<OrderReason[]> { return firstValueFrom(this.http.get<OrderReason[]>('/api/purchase-orders/reasons/cancel')); }
  close(order: Order, input: OrderTransitionInput): Promise<Order> { return firstValueFrom(this.http.post<Order>(`/api/purchase-orders/${order.id}/close`, { expectedVersion: order.version, ...input }, { context: input.authorization ? authorizationContext() : undefined })); }
  cancel(order: Order, input: OrderTransitionInput): Promise<Order> { return firstValueFrom(this.http.post<Order>(`/api/purchase-orders/${order.id}/cancel`, { expectedVersion: order.version, ...input }, { context: input.authorization ? authorizationContext() : undefined })); }
}
