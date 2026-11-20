import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthorizationBody, Page, authorizationContext } from '../../core/api';
import { type CashDrawerDispatch, withCashDrawerStatus } from '../../shared/cash-drawer-feedback';

export interface OperatorSession { id: string; state: string; }
export interface CashShift { id: string; mode: string; state: string; operatingDate: string; openedAtUtc: string; initialAmount: number; version: number; responsibleUserId: string | null; }
export interface CashState { isConfigured: boolean; hasOpenShift: boolean; defaultShiftMode: string | null; configurationVersion?: number | null; requiresPreviousShiftClosure: boolean; requiresOperatorSession: boolean; guidance: string | null; currentShift: CashShift | null; ownOperatorSession: OperatorSession | null; openShiftResponsibleName?: string | null; }
export interface ReconciliationMethod { paymentMethodId: string; code: string; name: string; affectsCash: boolean; expectedAmount: number; countedAmount: number | null; difference: number | null; }
export interface Reconciliation { shiftId: string; shiftVersion: number; totalExpected: number; totalCounted: number | null; totalDifference: number | null; paymentMethods: ReconciliationMethod[]; cashDifference: number | null; }
export interface CashMovement extends CashDrawerDispatch { id: string; type: string; category: string; amount: number; concept: string | null; occurredAtUtc: string; }
export interface CashDrawerStatus { enabled: boolean; profileName: string | null; canOpen: boolean; }
export interface CashDrawerOpenResult { status: 'ENVIADO' | 'FALLIDO' | 'INCIERTO'; message?: string | null; }
export interface ClosureDifferenceReason { id: string; code: string; name: string; isActive: boolean; }
export interface CashCloseInput {
  version: number;
  paymentMethods: { paymentMethodId: string; countedAmount: number }[];
  differenceReasonId: string | null;
  differenceObservation: string | null;
  authorization: AuthorizationBody | null;
}

@Injectable({ providedIn: 'root' })
export class CashApi {
  private readonly http = inject(HttpClient);
  state(): Promise<CashState> { return firstValueFrom(this.http.get<CashState>('/api/cash/state')); }
  /** A Cajero opens the cash with the authorization of a Supervisor or Administrador. */
  open(initialAmount: number, observations: string | null, authorization?: AuthorizationBody, mode?: 'INDIVIDUAL' | 'COMPARTIDO'): Promise<CashShift> {
    const body = { initialAmount, observations, ...(authorization ? { authorization } : {}), ...(mode ? { mode } : {}) };
    return firstValueFrom(this.http.post<CashShift>('/api/cash/shifts', body, { context: authorization ? authorizationContext() : undefined }));
  }
  movements(): Promise<Page<CashMovement>> { return firstValueFrom(this.http.get<Page<CashMovement>>('/api/cash/movements', { params: { page: 1, pageSize: 50 } })); }
  drawerStatus(): Promise<CashDrawerStatus> { return firstValueFrom(this.http.get<CashDrawerStatus>('/api/cash/drawer/status')); }
  /** Abre el cajón al pulsar «Cobrar» (19 de noviembre de 2026); una clave por intento de cobro. */
  openDrawerForCheckout(idempotencyKey: string): Promise<{ status: string; message?: string | null }> {
    return firstValueFrom(this.http.post<{ status: string; message?: string | null }>('/api/cash/drawer/checkout', null, { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) }));
  }
  openDrawer(reason: string, idempotencyKey: string): Promise<CashDrawerOpenResult> {
    return firstValueFrom(this.http.post<CashDrawerOpenResult>('/api/cash/drawer/open', { reason }, { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) }));
  }
  async movement(kind: 'incomes' | 'expenses' | 'cash-drops', amount: number, concept: string, operatorSessionId: string | null): Promise<CashMovement> {
    const response = await firstValueFrom(this.http.post<CashMovement>(`/api/cash/movements/${kind}`,
      { amount, concept, operatorSessionId }, { observe: 'response' }));
    return withCashDrawerStatus(response);
  }
  reconciliation(): Promise<Reconciliation> { return firstValueFrom(this.http.get<Reconciliation>('/api/cash/reconciliation')); }
  /** Creates the default closing-difference reasons that are missing and returns the active ones. */
  initializeClosureReasons(): Promise<ClosureDifferenceReason[]> {
    return firstValueFrom(this.http.post<ClosureDifferenceReason[]>('/api/cash/closure-difference-reasons/initialize', null));
  }
  closureDifferenceReasons(): Promise<ClosureDifferenceReason[]> {
    return firstValueFrom(this.http.get<ClosureDifferenceReason[]>('/api/cash/closure-difference-reasons'));
  }
  close(shiftId: string, input: CashCloseInput): Promise<Reconciliation> {
    return firstValueFrom(this.http.post<Reconciliation>(`/api/cash/shifts/${shiftId}/close`, input, { context: input.authorization ? authorizationContext() : undefined }));
  }
  /** INDIVIDUAL o COMPARTIDO; solo sin turno abierto. version = configurationVersion del estado. */
  updateShiftMode(mode: 'INDIVIDUAL' | 'COMPARTIDO', version: number): Promise<CashState> { return firstValueFrom(this.http.put<CashState>('/api/cash/shift-mode', { mode, version })); }
  /** Un carné inválido responde 401: se marca como intento de credencial para no cerrar la sesión del operador. */
  startSession(credentialToken: string): Promise<OperatorSession> { return firstValueFrom(this.http.post<OperatorSession>('/api/cash/operator-sessions', { credentialToken }, { context: authorizationContext() })); }
  endSession(id: string): Promise<OperatorSession> { return firstValueFrom(this.http.post<OperatorSession>(`/api/cash/operator-sessions/${id}/close`, {})); }
}
