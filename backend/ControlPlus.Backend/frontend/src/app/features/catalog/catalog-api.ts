import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, Page } from '../../core/api';
import { SuppliersApi } from '../suppliers/suppliers-api';

export interface Category { id: string; name: string; description: string | null; isActive: boolean; version: number; abbreviation: string | null; }
export interface Unit { id: string; code: string; name: string; symbol: string; isActive: boolean; }
export interface Supplier { id: string; name: string; isActive: boolean; }
export interface Product {
  id: string; categoryId: string; categoryName: string; measurementUnitId: string; measurementUnitCode: string;
  supplierId: string | null; internalCode: string; barcode: string | null; barcodeFormat: string | null; barcodeGenerated: boolean;
  name: string; description: string | null; retailPrice: number; wholesalePrice: number | null;
  averageCost: number | null; canViewCost: boolean; taxPercentage: number | null; currentStock: number;
  reservedStock: number; availableStock: number; minimumStock: number; isOutOfStock: boolean; isActive: boolean; version: number;
}
export type StockStatus = 'DISPONIBLE' | 'STOCK_BAJO' | 'AGOTADO';
/** Estado de existencias de un producto, en el mismo orden que el filtro: inactivo, agotado, stock bajo y disponible. */
export function productStockState(product: Pick<Product, 'isActive' | 'availableStock' | 'minimumStock'>): { label: string; tone: 'success' | 'warning' | 'error' | 'neutral' } {
  if (!product.isActive) return { label: 'Inactivo', tone: 'neutral' };
  if (product.availableStock <= 0) return { label: 'Agotado', tone: 'error' };
  if (product.availableStock <= product.minimumStock) return { label: 'Stock bajo', tone: 'warning' };
  return { label: 'Disponible', tone: 'success' };
}
export interface ProductInput {
  categoryId: string; measurementUnitId: string; supplierId: string | null;
  barcode: string | null; barcodeFormat: string | null; name: string; description: string | null;
  retailPrice: number; wholesalePrice: number | null; taxPercentage: number | null; minimumStock: number;
}
export interface CreateProductInput extends ProductInput { initialQuantity: number; initialUnitCost: number | null; }
export interface UpdateProductInput extends ProductInput { internalCode: string; }
export interface CostItem { purchaseId: string; documentDate: string; quantity: number; documentUnitCost: number; resultingAverageCost: number; }
export interface CostHistory { productId: string; currentAverageCost: number | null; lastConfirmedPurchaseCost: number | null; history: Page<CostItem>; }

@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private readonly http = inject(HttpClient);
  private readonly supplierApi = inject(SuppliersApi);
  categories(query: { search?: string; includeInactive?: boolean; page: number; pageSize: number }): Promise<Page<Category>> {
    return firstValueFrom(this.http.get<Page<Category>>('/api/categories', { params: this.params(query) }));
  }
  /** `stockStatus`: DISPONIBLE, STOCK_BAJO (incluye agotados) o AGOTADO, con el mismo criterio de Inventario (auditoría C.19). */
  products(query: { search?: string; categoryId?: string; includeInactive?: boolean; includeOutOfStock?: boolean; stockStatus?: StockStatus; page: number; pageSize: number }): Promise<Page<Product>> {
    return firstValueFrom(this.http.get<Page<Product>>('/api/products', { params: this.params(query) }));
  }
  units(): Promise<Unit[]> { return firstValueFrom(this.http.get<Unit[]>('/api/measurement-units')); }
  costHistory(productId: string, page: number): Promise<CostHistory> {
    return firstValueFrom(this.http.get<CostHistory>(`/api/products/${productId}/purchase-cost-history`, {
      params: this.params({ page, pageSize: 20 }),
    }));
  }
  async activeSuppliers(): Promise<Supplier[]> { return (await this.supplierApi.active()).map(s => ({ id: s.id, name: s.name, isActive: s.isActive })); }
  createCategory(name: string, description: string | null, abbreviation: string): Promise<Category> {
    return firstValueFrom(this.http.post<Category>('/api/categories', { name, description, abbreviation }));
  }
  // The server keeps the current abbreviation when the property is omitted.
  updateCategory(category: Category, name: string, description: string | null, abbreviation: string | null): Promise<Category> {
    return firstValueFrom(this.http.put<Category>(`/api/categories/${category.id}`, {
      name, description, version: category.version, ...(abbreviation ? { abbreviation } : {}),
    }));
  }
  setCategoryActive(category: Category): Promise<Category> {
    return firstValueFrom(this.http.put<Category>(`/api/categories/${category.id}/${category.isActive ? 'deactivate' : 'activate'}`, {}));
  }
  createProduct(input: CreateProductInput, generateBarcode: boolean, idempotencyKey: string): Promise<Product> {
    // New forms omit the internal code. A legacy pending snapshot must keep its
    // original value during recovery, since the server fingerprints that field.
    return firstValueFrom(this.http.post<Product>('/api/products', { internalCode: null, ...input, generateBarcode }, {
      headers: { 'Idempotency-Key': idempotencyKey },
    }));
  }
  /** Decisión D5: el Supervisor envía la autorización del Administrador; quien tiene PRODUCTS.SENSITIVE_UPDATE, ninguna. */
  updateProduct(product: Product, input: UpdateProductInput, authorization: AuthorizationBody | null = null): Promise<Product> {
    return firstValueFrom(this.http.put<Product>(`/api/products/${product.id}`,
      { ...input, barcodeGenerated: product.barcodeGenerated, version: product.version, ...(authorization ? { authorization } : {}) }));
  }
  setProductActive(product: Product): Promise<Product> {
    return firstValueFrom(this.http.put<Product>(`/api/products/${product.id}/${product.isActive ? 'deactivate' : 'activate'}`, {}));
  }
  /** Physical deletion: Administrator only, and only for a product without commercial history (the server decides). */
  deleteProduct(product: Product): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/products/${product.id}`, { params: { expectedVersion: product.version } }));
  }
  private params(query: Record<string, string | number | boolean | undefined>): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== '') params = params.set(key, String(value));
    return params;
  }
}
