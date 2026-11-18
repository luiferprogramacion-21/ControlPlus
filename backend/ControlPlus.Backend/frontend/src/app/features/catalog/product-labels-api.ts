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

export interface ProductLabelJobInput {
  profileId: string;
  lines: { productId: string; quantity: number }[];
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
}
