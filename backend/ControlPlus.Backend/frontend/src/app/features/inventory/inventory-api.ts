import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, authorizationContext } from '../../core/api';
import { Page } from '../../core/api';
export interface Stock { productId: string; internalCode: string; barcode: string | null; name: string; measurementUnit: string; currentStock: number; reservedStock: number; availableStock: number; minimumStock: number; isOutOfStock: boolean; isActive: boolean; averageCost: number | null; lastMovementType: string | null; lastMovementAtUtc: string | null; version: number; }
export interface Movement { id: string; productId: string; productCode: string; productName: string; type: string; stockQuantity: number; previousStock: number; newStock: number; reservedQuantity: number; reasonName: string | null; observation: string | null; occurredAtUtc: string; previousAverageCost: number | null; newAverageCost: number | null; }
export type StockStatusFilter = '' | 'DISPONIBLE' | 'STOCK_BAJO' | 'AGOTADO' | 'INACTIVO';
export interface Reason { id: string; code: string; name: string; description: string | null; displayOrder: number; }
export interface Adjustment { productId: string; quantityDelta: number; reasonId: string; expectedVersion: number; observation: string | null; authorization?: AuthorizationBody; }
@Injectable({ providedIn: 'root' })
export class InventoryApi {
  private readonly http = inject(HttpClient);
  /** `status` vacío lista los activos; los demás valores filtran por estado (STOCK_BAJO incluye los agotados). */
  stock(params: { search: string; status: StockStatusFilter; page: number; pageSize: number }): Promise<Page<Stock>> {
    let query = new HttpParams().set('page', params.page).set('pageSize', params.pageSize);
    if (params.search) query = query.set('search', params.search);
    if (params.status) query = query.set('status', params.status);
    return firstValueFrom(this.http.get<Page<Stock>>('/api/inventory/stock', { params: query }));
  }
  movements(params: { productId: string; type?: string; fromUtc?: string; toUtc?: string; page: number; pageSize: number }): Promise<Page<Movement>> {
    let query = new HttpParams()
      .set('productId', params.productId)
      .set('page', params.page)
      .set('pageSize', params.pageSize);
    if (params.type) query = query.set('type', params.type);
    if (params.fromUtc) query = query.set('fromUtc', params.fromUtc);
    if (params.toUtc) query = query.set('toUtc', params.toUtc);
    return firstValueFrom(this.http.get<Page<Movement>>('/api/inventory/movements', { params: query }));
  }
  reasons(): Promise<Reason[]> { return firstValueFrom(this.http.get<Reason[]>('/api/inventory/adjustment-reasons')); }
  adjust(input: Adjustment, key: string): Promise<Movement> { return firstValueFrom(this.http.post<Movement>('/api/inventory/adjustments', input, { headers: new HttpHeaders({ 'Idempotency-Key': key }), context: input.authorization ? authorizationContext() : undefined })); }
}
