import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Numbering { documentType?: string; series: string; prefix: string; lastNumber: number; nextNumber: number; version: number; canCorrect?: boolean; }

/** A document type that has its own visible numbering. */
export interface NumberingKind {
  key: string; label: string; path: string; defaultPrefix: string;
  /** Exceptional endpoints used by document types whose numbering is initialized by their own module. */
  endpoint?: string; initializeEndpoint?: string;
  /** The server fixes the prefix of every type except sales. */
  fixedPrefix: boolean;
  /** Types with PUT can be corrected until their first document exists. */
  canEdit: boolean; editNote: string;
}

export const NUMBERING_KINDS: NumberingKind[] = [
  { key: 'sales', label: 'Ventas', path: 'sales', defaultPrefix: 'VTA', fixedPrefix: false, canEdit: true, editNote: 'Solo se puede cambiar antes de confirmar la primera venta.' },
  { key: 'credit-payments', label: 'Abonos de crédito', path: 'credit-payments', defaultPrefix: 'ABO', fixedPrefix: true, canEdit: true, editNote: 'Solo se puede ajustar el próximo número antes del primer abono.' },
  { key: 'layaways', label: 'Apartados', path: 'layaways', defaultPrefix: 'APT', fixedPrefix: true, canEdit: false, editNote: 'Una vez creado, no se puede modificar.' },
  { key: 'layaway-payments', label: 'Abonos de apartado', path: 'layaway-payments', defaultPrefix: 'PAP', fixedPrefix: true, canEdit: false, editNote: 'Una vez creado, no se puede modificar.' },
  { key: 'purchases', label: 'Compras', path: 'purchases', defaultPrefix: 'CMP', fixedPrefix: true, canEdit: true, editNote: 'Solo se puede ajustar el próximo número antes de la primera compra confirmada.' },
  { key: 'purchase-orders', label: 'Pedidos de compra', path: 'purchase-orders', endpoint: '/api/purchase-orders/numbering', initializeEndpoint: '/api/purchase-orders/numbering/initialize', defaultPrefix: 'PED', fixedPrefix: true, canEdit: false, editNote: 'Empieza en 1 y, una vez creado, no se puede modificar.' },
  { key: 'sale-changes', label: 'Cambios de venta', path: 'sale-changes', defaultPrefix: 'CAM', fixedPrefix: true, canEdit: false, editNote: 'Una vez creado, no se puede modificar. Sin este consecutivo no se pueden registrar cambios de productos.' },
];

export interface ChangePolicy { days: number; version: number; }
/** `requireMinimumInitial` off means any initial payment above zero is accepted; the percentage is kept for when it is switched on again. */
export interface LayawaySettings { minimumPercentage: number; defaultDays: number | null; requireMinimumInitial: boolean; version: number; }

/** Business-wide VAT switch. `percentage` is null until the Administrator sets it for the first time. */
export interface VatSettings { enabled: boolean; percentage: number | null; version: number; updatedAtUtc: string | null; }

/** Default due-date term of a credit sale in days (1 to 90); null means the seller must choose the date each time. */
export interface CreditSettings { defaultDays: number | null; version: number; }

export interface ReasonItem { id: string; code: string; name: string; displayOrder?: number; isActive?: boolean; }
export interface ReasonGroup { key: string; label: string; list: string; initialize: string; }
export const REASON_GROUPS: ReasonGroup[] = [
  { key: 'sale', label: 'Anulación de ventas', list: '/api/sales/cancellation-reasons', initialize: '/api/sales/cancellation-reasons/initialize' },
  { key: 'purchase', label: 'Anulación de compras', list: '/api/purchases/cancellation-reasons', initialize: '/api/purchases/cancellation-reasons/initialize' },
  { key: 'order-close', label: 'Cierre de pedidos de compra', list: '/api/purchase-orders/reasons/close', initialize: '/api/purchase-orders/reasons/close/initialize' },
  { key: 'order-cancel', label: 'Cancelación de pedidos de compra', list: '/api/purchase-orders/reasons/cancel', initialize: '/api/purchase-orders/reasons/cancel/initialize' },
];

@Injectable({ providedIn: 'root' })
export class ConfigApi {
  private readonly http = inject(HttpClient);
  private url(kind: NumberingKind): string { return kind.endpoint ?? `/api/document-numbering/${kind.path}`; }

  /** Resolves null when the numbering has not been configured yet (the server answers 404). */
  async numbering(kind: NumberingKind): Promise<Numbering | null> {
    try { return await firstValueFrom(this.http.get<Numbering>(this.url(kind))); }
    catch (error) { if (error instanceof HttpErrorResponse && error.status === 404) return null; throw error; }
  }
  createNumbering(kind: NumberingKind, prefix: string, firstNumber: number): Promise<Numbering> {
    if (kind.initializeEndpoint) return firstValueFrom(this.http.post<Numbering>(kind.initializeEndpoint, null));
    return firstValueFrom(this.http.post<Numbering>(this.url(kind), { prefix, firstNumber }));
  }
  updateNumbering(kind: NumberingKind, prefix: string, firstNumber: number, version: number): Promise<Numbering> {
    return firstValueFrom(this.http.put<Numbering>(this.url(kind), { prefix, firstNumber, version }));
  }

  changePolicy(): Promise<ChangePolicy> { return firstValueFrom(this.http.get<ChangePolicy>('/api/sales/change-policy')); }
  updateChangePolicy(days: number, expectedVersion: number): Promise<ChangePolicy> {
    return firstValueFrom(this.http.put<ChangePolicy>('/api/sales/change-policy', { days, expectedVersion }));
  }

  layawaySettings(): Promise<LayawaySettings> { return firstValueFrom(this.http.get<LayawaySettings>('/api/layaways/settings')); }
  updateLayawaySettings(minimumPercentage: number, defaultDays: number | null, requireMinimumInitial: boolean, expectedVersion: number): Promise<LayawaySettings> {
    return firstValueFrom(this.http.put<LayawaySettings>('/api/layaways/settings', { minimumPercentage, defaultDays, requireMinimumInitial, expectedVersion }));
  }

  creditSettings(): Promise<CreditSettings> { return firstValueFrom(this.http.get<CreditSettings>('/api/credits/settings')); }
  updateCreditSettings(defaultDays: number | null, expectedVersion: number): Promise<CreditSettings> {
    return firstValueFrom(this.http.put<CreditSettings>('/api/credits/settings', { defaultDays, expectedVersion }));
  }

  vatSettings(): Promise<VatSettings> { return firstValueFrom(this.http.get<VatSettings>('/api/settings/vat')); }
  updateVatSettings(enabled: boolean, percentage: number | null, expectedVersion: number): Promise<VatSettings> {
    return firstValueFrom(this.http.put<VatSettings>('/api/settings/vat', { enabled, percentage, expectedVersion }));
  }

  reasons(group: ReasonGroup): Promise<ReasonItem[]> { return firstValueFrom(this.http.get<ReasonItem[]>(group.list)); }
  initializeReasons(group: ReasonGroup): Promise<ReasonItem[]> { return firstValueFrom(this.http.post<ReasonItem[]>(group.initialize, {})); }
}
