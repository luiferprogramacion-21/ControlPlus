import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, authorizationContext } from '../../core/api';
import { Page } from '../../core/api';
import { Customer, CustomersApi } from '../customers/customers-api';
import type { SaleReceipt } from './ticket';

export interface SaleDiscount { type: 'PORCENTAJE' | 'VALOR_FIJO'; value: number; }
export interface SaleLineInput { productId: string; quantity: number; priceType: 'MINORISTA' | 'MAYORISTA'; discount: SaleDiscount | null; }
export interface SalePaymentInput { methodCode: 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI'; appliedAmount: number; receivedAmount: number | null; reference: string | null; }
export interface SaleInput { customerId: string | null; lines: SaleLineInput[]; generalDiscount: SaleDiscount | null; discountReason: string | null; payments: SalePaymentInput[]; saleType: 'CONTADO' | 'CREDITO'; dueDate: string | null; authorization: AuthorizationBody | null; }
export interface Sale {
  id: string; visibleNumber: string; total: number; change: number; state: string; payments: (SalePaymentInput & { methodName?: string })[]; creditId: string | null;
  occurredAtUtc?: string; grossSubtotal?: number; discountTotal?: number; lines?: SaleLine[];
}
export interface SaleReprintRequest { requestId: string; saleId: string; requestedAtUtc: string; status: string; }

export interface SaleSummary { id: string; visibleNumber: string; userId: string; customerId: string | null; occurredAtUtc: string; operatingDate: string; total: number; state: string; }
/** Default due-date term of a credit sale in days; null when the seller must choose the date. The range of 1 to 90 days is enforced by the server. */
export interface CreditRules { defaultDays: number | null; }
export interface SaleLine { id: string; productId: string; productCode: string; productName: string; measurementUnit: string; quantity: number; priceType: string; unitPrice: number; discountAmount: number; grossAmount: number; netAmount: number; }
export interface SalePaymentLine { id: string; methodCode: string; methodName: string; appliedAmount: number; receivedAmount: number | null; change: number; reference: string | null; }
export interface SaleDetail { id: string; visibleNumber: string; occurredAtUtc: string; operatingDate: string; grossSubtotal: number; discountTotal: number; total: number; change: number; state: string; version: number; creditId: string | null; lines: SaleLine[]; payments: SalePaymentLine[]; }
export interface SaleCancellationReason { id: string; code: string; name: string; displayOrder: number; }
export interface CancelSaleInput { expectedVersion: number; reasonId: string; observation: string | null; authorization?: AuthorizationBody; }
export interface CancelSaleResult { saleId: string; visibleNumber: string; state: string; version: number; saleType: string; reversedAmount: number; }

export interface ChangeLineAvailability { saleLineId: string; productId: string; productCode: string; productName: string; soldQuantity: number; returnedQuantity: number; remainingQuantity: number; }
export interface ChangePreparation {
  saleId: string; visibleNumber: string; saleType: 'CONTADO' | 'CREDITO'; saleState: string; saleVersion: number; operatingDate: string; changeDeadline: string;
  currentLocalDate: string; withinDeadline: boolean; canStartChange: boolean; lines: ChangeLineAvailability[];
  creditId: string | null; creditVersion: number | null; creditBalance: number | null;
}
export interface ChangePaymentInput { methodCode: string; amount: number; receivedAmount: number | null; reference: string | null; }
export interface ConfirmChangeInput {
  expectedSaleVersion: number; expectedCreditVersion: number | null;
  returned: { saleLineId: string; quantity: number; resellable: boolean }[];
  delivered: { productId: string; quantity: number; priceType: 'MINORISTA' | 'MAYORISTA' }[];
  treatment: string; payments: ChangePaymentInput[]; authorization?: AuthorizationBody; reason?: string | null;
}
export interface ConfirmedChange { id: string; saleId: string; visibleNumber: string; totalReturned: number; totalDelivered: number; difference: number; treatment: string; creditAdjustment: number; paidOrRefunded: number; }

export interface PrintProfile { id: string; name: string; systemQueueName: string; columns: number; isDefault: boolean; active: boolean; version: number; }
export interface PrintStatus { hasActiveProfile: boolean; profileName: string | null; columns: number | null; }
export interface CreatePrintProfileInput { name: string; systemQueueName: string; columns: number; makeDefault?: boolean; }
export type PrintJobStatus = 'PENDIENTE' | 'IMPRIMIENDO' | 'IMPRESO' | 'ERROR';
export interface PrintJob { id: string; saleId: string; copyType: 'ORIGINAL' | 'REIMPRESION'; copyNumber: number; status: PrintJobStatus; attempts: number; requestedAtUtc: string; queueAcceptedAtUtc: string | null; error: string | null; }
export interface SaleReprintRequest { requestId: string; saleId: string; requestedAtUtc: string; status: string; }

/** Texto para el usuario. «IMPRESO» solo indica que la cola de Windows aceptó el trabajo, no que haya salido papel. */
export function printJobStatusText(job: Pick<PrintJob, 'status' | 'error'>): string {
  switch (job.status) {
    case 'PENDIENTE': return 'Esperando envío a la cola.';
    case 'IMPRIMIENDO': return job.error ? 'Resultado incierto: revisa la cola de la impresora antes de pedir otra copia.' : 'Envío en curso.';
    case 'IMPRESO': return 'Aceptado por la cola de Windows.';
    case 'ERROR': return 'La cola rechazó el trabajo o falló la verificación.';
    default: return job.status;
  }
}

@Injectable({ providedIn: 'root' })
export class SalesApi {
  private readonly http = inject(HttpClient);
  private readonly customersApi = inject(CustomersApi);
  confirm(input: SaleInput, key: string): Promise<Sale> {
    return firstValueFrom(this.http.post<Sale>('/api/sales', input, { headers: new HttpHeaders({ 'Idempotency-Key': key }), context: input.authorization ? authorizationContext() : undefined }));
  }
  get(id: string): Promise<Sale> { return firstValueFrom(this.http.get<Sale>(`/api/sales/${id}`)); }
  /** Checks the credentials of the Supervisor/Administrador who authorizes a discount or a wholesale price; nothing is stored. */
  verifyConcession(kind: 'DISCOUNT' | 'WHOLESALE', userName: string, password: string, credentialToken?: string): Promise<{ authorizedBy: string }> {
    const body = credentialToken ? { credentialToken, kind } : { userName, password, kind };
    return firstValueFrom(this.http.post<{ authorizedBy: string }>('/api/sales/concession-authorizations/verify', body, { context: authorizationContext() }));
  }
  list(all: boolean, page: number, pageSize: number): Promise<Page<SaleSummary>> {
    return firstValueFrom(this.http.get<Page<SaleSummary>>(all ? '/api/sales' : '/api/sales/own', { params: { page, pageSize } }));
  }
  detail(id: string): Promise<SaleDetail> { return firstValueFrom(this.http.get<SaleDetail>(`/api/sales/${id}`)); }
  cancellationReasons(): Promise<SaleCancellationReason[]> { return firstValueFrom(this.http.get<SaleCancellationReason[]>('/api/sales/cancellation-reasons')); }
  cancel(saleId: string, input: CancelSaleInput, key: string): Promise<CancelSaleResult> {
    return firstValueFrom(this.http.post<CancelSaleResult>(`/api/sales/${saleId}/cancel`, input, { headers: new HttpHeaders({ 'Idempotency-Key': key }), context: input.authorization ? authorizationContext() : undefined }));
  }
  changePreparation(saleId: string): Promise<ChangePreparation> { return firstValueFrom(this.http.get<ChangePreparation>(`/api/sales/${saleId}/change-preparation`)); }
  confirmChange(saleId: string, input: ConfirmChangeInput, key: string): Promise<ConfirmedChange> {
    return firstValueFrom(this.http.post<ConfirmedChange>(`/api/sales/${saleId}/changes`, input, { headers: new HttpHeaders({ 'Idempotency-Key': key }), context: input.authorization ? authorizationContext() : undefined }));
  }
  creditRules(): Promise<CreditRules> { return firstValueFrom(this.http.get<CreditRules>('/api/sales/credit-rules')); }
  customers(search: string): Promise<Page<Customer>> { return this.customersApi.list({ search, status: 'active', page: 1, pageSize: 20 }); }
  printJobs(saleId: string, page = 1, pageSize = 20): Promise<Page<PrintJob>> { return firstValueFrom(this.http.get<Page<PrintJob>>(`/api/sales/${saleId}/print-jobs`, { params: { page, pageSize } })); }
  printingStatus(): Promise<PrintStatus> { return firstValueFrom(this.http.get<PrintStatus>('/api/printing/status')); }
  receipt(saleId: string): Promise<SaleReceipt> { return firstValueFrom(this.http.get<SaleReceipt>(`/api/sales/${saleId}/receipt`)); }
  requestReprint(saleId: string, key: string): Promise<SaleReprintRequest> {
    return firstValueFrom(this.http.post<SaleReprintRequest>(`/api/sales/${saleId}/reprint-requests`, null, { headers: new HttpHeaders({ 'Idempotency-Key': key }) }));
  }
  retryPrintJob(saleId: string, jobId: string): Promise<PrintJob> { return firstValueFrom(this.http.post<PrintJob>(`/api/sales/${saleId}/print-jobs/${jobId}/retry`, null)); }
  retryAdminPrintJob(jobId: string): Promise<PrintJob> { return firstValueFrom(this.http.post<PrintJob>(`/api/printing/jobs/${jobId}/retry`, null)); }
  printProfiles(): Promise<PrintProfile[]> { return firstValueFrom(this.http.get<PrintProfile[]>('/api/printing/profiles')); }
  createPrintProfile(input: CreatePrintProfileInput): Promise<PrintProfile> { return firstValueFrom(this.http.post<PrintProfile>('/api/printing/profiles', input)); }
  printQueue(page = 1, pageSize = 20): Promise<Page<PrintJob>> { return firstValueFrom(this.http.get<Page<PrintJob>>('/api/printing/jobs', { params: { page, pageSize } })); }
}
