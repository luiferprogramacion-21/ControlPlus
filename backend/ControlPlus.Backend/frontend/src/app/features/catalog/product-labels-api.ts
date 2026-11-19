import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Page } from '../../core/api';

export interface ProductLabelProfile {
  id: string;
  name: string;
  printerType: 'ETIQUETAS' | 'HIBRIDA';
  widthMm: number | null;
  heightMm: number | null;
  resolutionDpi: number | null;
  isDefault: boolean;
  labelTemplateName: string;
}

export interface ProductLabelProduct {
  id: string;
  internalCode: string;
  barcode: string | null;
  name: string;
  retailPrice: number;
}

export interface ProductLabelJob {
  jobId: string;
  batchId: string;
  profileId: string;
  profileName: string;
  labelCount: number;
  status: string;
  requestedAtUtc: string;
}

export interface ProductLabelQueueItem extends ProductLabelJob {
  sourcePurchaseId: string | null;
  error: string | null;
}

export interface ProductLabelJobInput {
  profileId: string;
  lines: { productId: string; quantity: number }[];
  sourcePurchaseId?: string;
}

export interface ProductLabelReceipt {
  purchaseId: string;
  visibleNumber: string;
  supplierName: string;
  receivedAtUtc: string;
  pendingLabelCount: number;
  lines: { product: ProductLabelProduct; receivedQuantity: number; pendingQuantity: number }[];
}

@Injectable({ providedIn: 'root' })
export class ProductLabelsApi {
  private readonly http = inject(HttpClient);

  profiles(): Promise<ProductLabelProfile[]> {
    return firstValueFrom(this.http.get<ProductLabelProfile[]>('/api/product-labels/profiles'));
  }

  products(search: string, page = 1, pageSize = 20): Promise<Page<ProductLabelProduct>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search.trim()) params = params.set('search', search.trim());
    return firstValueFrom(this.http.get<Page<ProductLabelProduct>>('/api/product-labels/products', { params }));
  }

  queue(input: ProductLabelJobInput, idempotencyKey: string): Promise<ProductLabelJob> {
    return firstValueFrom(this.http.post<ProductLabelJob>('/api/product-labels/jobs', input, {
      headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
    }));
  }

  receipts(page = 1, pageSize = 20): Promise<Page<ProductLabelReceipt>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return firstValueFrom(this.http.get<Page<ProductLabelReceipt>>('/api/product-labels/receipts', { params }));
  }

  receipt(purchaseId: string): Promise<ProductLabelReceipt> {
    return firstValueFrom(this.http.get<ProductLabelReceipt>(`/api/product-labels/receipts/${encodeURIComponent(purchaseId)}`));
  }

  jobs(page = 1, pageSize = 20): Promise<Page<ProductLabelQueueItem>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return firstValueFrom(this.http.get<Page<ProductLabelQueueItem>>('/api/product-labels/jobs', { params }));
  }

  retry(jobId: string): Promise<ProductLabelQueueItem> {
    return firstValueFrom(this.http.post<ProductLabelQueueItem>(`/api/product-labels/jobs/${encodeURIComponent(jobId)}/retry`, {}));
  }
}
