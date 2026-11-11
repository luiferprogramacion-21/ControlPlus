import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, authorizationContext } from '../../core/api';
import { Page } from '../../core/api';
export interface Stock { productId: string; internalCode: string; barcode: string | null; name: string; measurementUnit: string; currentStock: number; reservedStock: number; availableStock: number; minimumStock: number; isOutOfStock: boolean; isActive: boolean; averageCost: number | null; lastMovementType: string | null; lastMovementAtUtc: string | null; version: number; }
export interface Movement { id: string; productId: string; productCode: string; productName: string; type: string; stockQuantity: number; previousStock: number; newStock: number; reservedQuantity: number; reasonName: string | null; observation: string | null; occurredAtUtc: string; previousAverageCost: number | null; newAverageCost: number | null; }
export interface Reason { id: string; code: string; name: string; description: string | null; displayOrder: number; }
export interface Adjustment { productId: string; quantityDelta: number; reasonId: string; expectedVersion: number; observation: string | null; authorization?: AuthorizationBody; }
@Injectable({ providedIn: 'root' })
export class InventoryApi {
  private readonly http = inject(HttpClient);
  stock(params: { search: string; includeInactive: boolean; page: number; pageSize: number }): Promise<Page<Stock>> { return firstValueFrom(this.http.get<Page<Stock>>('/api/inventory/stock', { params })); }
  movements(params: { productId: string; type?: string; fromUtc?: string; toUtc?: string; page: number; pageSize: number }): Promise<Page<Movement>> { return firstValueFrom(this.http.get<Page<Movement>>('/api/inventory/movements', { params })); }
  reasons(): Promise<Reason[]> { return firstValueFrom(this.http.get<Reason[]>('/api/inventory/adjustment-reasons')); }
  adjust(input: Adjustment, key: string): Promise<Movement> { return firstValueFrom(this.http.post<Movement>('/api/inventory/adjustments', input, { headers: new HttpHeaders({ 'Idempotency-Key': key }), context: input.authorization ? authorizationContext() : undefined })); }
}
