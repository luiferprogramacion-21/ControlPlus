import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, Page } from '../../core/api';
import { type CashDrawerDispatch, withCashDrawerStatus } from '../../shared/cash-drawer-feedback';

/** Layaway states: ACTIVO, PAGADO, ENTREGADO, ANULADO. Overdue is a flag, never a state. */
export interface Layaway {
  id: string; number: string; customerId: string; customerName: string; createdAtUtc: string; dueOn: string;
  total: number; initialPayment: number; balance: number; state: string; version: number; isOverdue: boolean;
}
/** What whoever creates a layaway needs to know: the minimum initial payment rule and the default term of the due date. */
export interface LayawayCreationRules { requireMinimumInitial: boolean; minimumPercentage: number; defaultDays: number | null; }
export interface LayawayLine { productId: string; productCode: string; productName: string; unit: string; quantity: number; unitPrice: number; subtotal: number; }
export interface LayawayPayment {
  id: string; number: string; methodCode: string; methodName: string; appliedAmount: number; receivedAmount: number | null;
  change: number; occurredAtUtc: string; state: string; correlationId: string | null;
}
export interface DeliveredProduct { productId: string; productCode: string; productName: string; unit: string; quantity: number; }
export interface LayawayDelivery {
  layawayId: string; number: string; state: string; version: number; deliveredAtUtc: string;
  deliveredByUserId: string; correlationId: string; products: DeliveredProduct[];
}
export interface LayawayRefund { id: string; methodCode: string; amount: number; reference: string | null; occurredAtUtc: string; }
export interface LayawayCancellation extends CashDrawerDispatch {
  layawayId: string; number: string; state: string; version: number; totalPaid: number; refunded: number; retained: number;
  cancelledAtUtc: string; cancelledByUserId: string; authorizedByUserId: string; reasonId: string | null;
  observation: string | null; correlationId: string; refunds: LayawayRefund[];
}
export interface LayawayDetail {
  layaway: Layaway; initialMinimumPercentage: number; lines: LayawayLine[]; payments: LayawayPayment[];
  delivery: LayawayDelivery | null; cancellation: LayawayCancellation | null;
}
export interface LayawayFilters {
  page: number; pageSize: number; state?: string; customerId?: string; dueFrom?: string; dueTo?: string; overdue?: boolean;
}
export interface LayawayPaymentInput { methodCode: 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI'; appliedAmount: number; receivedAmount: number | null; reference: string | null; }
export type LayawayPriceType = 'MINORISTA' | 'MAYORISTA';
export type LayawayAuthorization = AuthorizationBody;
export interface CreateLayawayLineInput { productId: string; quantity: number; priceType: LayawayPriceType; }
export interface CreateLayawayInput {
  customerId: string; lines: CreateLayawayLineInput[]; payments: LayawayPaymentInput[]; dueDate: string | null;
  /** Required for Cajero and Supervisor; an Administrator must omit it (the server rejects credentials from one). */
  authorization?: LayawayAuthorization;
}
export interface CreatedLayawayLine { productId: string; productCode: string; productName: string; measurementUnit: string; quantity: number; priceType: string; unitPrice: number; total: number; }
export interface CreatedLayaway extends CashDrawerDispatch {
  id: string; visibleNumber: string; customerId: string; shiftId: string; userId: string; operatorSessionId: string | null;
  dueDate: string; minimumPercentage: number; minimumInitialPayment: number; initialPayment: number; total: number; balance: number;
  change: number; state: string; version: number; lines: CreatedLayawayLine[]; payments: unknown[];
}
export interface RefundInput { methodCode: LayawayPaymentInput['methodCode']; amount: number; reference: string | null; }
/** Corrección de productos de un apartado sin entregar: las líneas finales; 0 o ausente quita el producto. */
export interface LayawayProductCorrectionInput {
  expectedVersion: number; reason: string; lines: { productId: string; quantity: number }[]; refunds: RefundInput[];
  authorization?: LayawayAuthorization;
}
export interface LayawayProductCorrection {
  id: string; layawayId: string; version: number; previousTotal: number; total: number; previousBalance: number;
  balance: number; refunded: number; reason: string; occurredAtUtc: string; correlationId: string;
}
export interface CancelLayawayInput {
  expectedVersion: number; refundAmount: number; refunds: RefundInput[]; reasonId: string | null; observation: string | null; authorization?: LayawayAuthorization;
}
export interface LayawayPaymentResult extends CashDrawerDispatch { layawayId: string; balance: number; state: string; version: number; correlationId: string; change: number; payments: unknown[]; }
export interface LayawayPaymentCancellationReason { id: string; code: string; name: string; displayOrder: number; }
export interface CancelLayawayPaymentInput { expectedVersion: number; reasonId: string; observation: string | null; authorization?: LayawayAuthorization; }
export interface CancelLayawayPaymentResult { layawayId: string; balance: number; state: string; version: number; reversedAmount: number; correlationId: string; cancelledPaymentIds: string[]; }

@Injectable({ providedIn: 'root' })
export class LayawaysApi {
  private readonly http = inject(HttpClient);
  list(filters: LayawayFilters): Promise<Page<Layaway>> {
    const params: Record<string, string | number | boolean> = { page: filters.page, pageSize: filters.pageSize };
    for (const key of ['state', 'customerId', 'dueFrom', 'dueTo'] as const) if (filters[key]) params[key] = filters[key]!;
    if (filters.overdue !== undefined) params['overdue'] = filters.overdue;
    return firstValueFrom(this.http.get<Page<Layaway>>('/api/layaways', { params }));
  }
  get(id: string): Promise<LayawayDetail> { return firstValueFrom(this.http.get<LayawayDetail>(`/api/layaways/${id}`)); }
  paymentCancellationReasons(): Promise<LayawayPaymentCancellationReason[]> {
    return firstValueFrom(this.http.get<LayawayPaymentCancellationReason[]>('/api/layaways/payment-cancellation-reasons'));
  }
  /** Corrige los productos de un apartado sin entregar (decisión del 19 de noviembre de 2026). */
  correctProducts(layaway: Layaway, input: LayawayProductCorrectionInput, key: string): Promise<LayawayProductCorrection> {
    return firstValueFrom(this.http.post<LayawayProductCorrection>(`/api/layaways/${layaway.id}/product-corrections`, input,
      { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
  /** Correcciones de productos ya hechas a un apartado, para verlas en su detalle (pruebas del usuario del 26/11/2026). */
  productCorrections(layawayId: string): Promise<LayawayProductCorrection[]> {
    return firstValueFrom(this.http.get<LayawayProductCorrection[]>(`/api/layaways/${layawayId}/product-corrections`));
  }
  cancelPayment(layaway: Layaway, paymentId: string, input: CancelLayawayPaymentInput, key: string): Promise<CancelLayawayPaymentResult> {
    return firstValueFrom(this.http.post<CancelLayawayPaymentResult>(`/api/layaways/${layaway.id}/payments/${paymentId}/cancel`, input,
      { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
  async applyPayment(layaway: Layaway, payments: LayawayPaymentInput[], key: string): Promise<LayawayPaymentResult> {
    const response = await firstValueFrom(this.http.post<LayawayPaymentResult>(`/api/layaways/${layaway.id}/payments`,
      { expectedVersion: layaway.version, payments },
      { headers: new HttpHeaders({ 'Idempotency-Key': key }), observe: 'response' }));
    return withCashDrawerStatus(response);
  }
  creationRules(): Promise<LayawayCreationRules> { return firstValueFrom(this.http.get<LayawayCreationRules>('/api/layaways/creation-rules')); }
  /** Units of the same product required for the wholesale price (configured by the Administrator). */
  pricingRules(): Promise<{ wholesaleMinimumQuantity: number }> { return firstValueFrom(this.http.get<{ wholesaleMinimumQuantity: number }>('/api/sales/pricing-rules')); }
  async create(input: CreateLayawayInput, key: string): Promise<CreatedLayaway> {
    const response = await firstValueFrom(this.http.post<CreatedLayaway>('/api/layaways', input,
      { headers: new HttpHeaders({ 'Idempotency-Key': key }), observe: 'response' }));
    return withCashDrawerStatus(response);
  }
  async cancel(layaway: Layaway, input: Omit<CancelLayawayInput, 'expectedVersion'>, key: string): Promise<LayawayCancellation> {
    const response = await firstValueFrom(this.http.post<LayawayCancellation>(`/api/layaways/${layaway.id}/cancellation`,
      { expectedVersion: layaway.version, ...input },
      { headers: new HttpHeaders({ 'Idempotency-Key': key }), observe: 'response' }));
    return withCashDrawerStatus(response);
  }
  deliver(layaway: Layaway, key: string): Promise<LayawayDelivery> {
    return firstValueFrom(this.http.post<LayawayDelivery>(`/api/layaways/${layaway.id}/delivery`,
      { expectedVersion: layaway.version }, { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
}
