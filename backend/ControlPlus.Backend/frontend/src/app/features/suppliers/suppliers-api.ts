import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, Page } from '../../core/api';
export interface Supplier { id: string; documentType: string | null; documentNumber: string | null; name: string; phone: string | null; address: string | null; email: string | null; observations: string | null; isActive: boolean; version: number; }
export type SupplierInput = Pick<Supplier, 'documentType' | 'documentNumber' | 'name' | 'phone' | 'address' | 'email' | 'observations'>;
@Injectable({ providedIn: 'root' })
export class SuppliersApi {
  private readonly http = inject(HttpClient);
  list(query: { name?: string; documentType?: string; documentNumber?: string; isActive?: boolean | null; page?: number; pageSize?: number } = {}): Promise<Page<Supplier>> {
    const params: Record<string, string | number | boolean> = { page: query.page ?? 1, pageSize: query.pageSize ?? 20 };
    if (query.name) params['name'] = query.name;
    if (query.documentType) params['documentType'] = query.documentType;
    if (query.documentNumber) params['documentNumber'] = query.documentNumber;
    if (query.isActive != null) params['isActive'] = query.isActive;
    return firstValueFrom(this.http.get<Page<Supplier>>('/api/suppliers', { params }));
  }
  get(id: string): Promise<Supplier> { return firstValueFrom(this.http.get<Supplier>(`/api/suppliers/${id}`)); }
  /** Un Supervisor envía la autorización del Administrador (pruebas del usuario del 26/11/2026). */
  create(input: SupplierInput, authorization?: AuthorizationBody): Promise<Supplier> { return firstValueFrom(this.http.post<Supplier>('/api/suppliers', authorization ? { ...input, authorization } : input)); }
  update(supplier: Supplier, input: SupplierInput): Promise<Supplier> { return firstValueFrom(this.http.put<Supplier>(`/api/suppliers/${supplier.id}`, { ...input, expectedVersion: supplier.version })); }
  status(supplier: Supplier): Promise<Supplier> { return firstValueFrom(this.http.put<Supplier>(`/api/suppliers/${supplier.id}/status`, { isActive: !supplier.isActive, expectedVersion: supplier.version })); }
  async active(): Promise<Supplier[]> { const result: Supplier[] = []; let page = 1; let total = 1; do { const response = await this.list({ isActive: true, page, pageSize: 100 }); result.push(...response.items); total = response.totalPages; page++; } while (page <= total); return result; }
}
