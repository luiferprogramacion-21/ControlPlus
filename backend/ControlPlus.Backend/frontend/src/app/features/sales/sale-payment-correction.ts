import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText, reasonOr } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { WindowSheet } from '../../shared/window-sheet';
import { appliedTotal, money, PaymentDraft, PaymentMethod, PaymentMethods, toPayments, validateDrafts } from '../layaways/payment-methods';
import { CorrectedPayments, SaleCancellationReason, SaleDetail, SalesApi } from './sales-api';

interface Pending { key: string; fingerprint: string; authorizerUserName: string | null; }

/**
 * «Corregir medio de pago» (20 de noviembre de 2026): la venta se cobró con un medio y se registró con otro, por ejemplo
 * pagaron por Nequi y quedó como efectivo. La venta, sus productos y su total no cambian; solo los medios. Decisión D3
 * del 23/11/2026: también en ventas de una caja ya cerrada; ese cierre no cambia y la diferencia se mueve en la caja de hoy.
 */
@Component({
  selector: 'cp-sale-payment-correction',
  imports: [FormsModule, PaymentMethods, WindowSheet],
  template: `
    <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="payment-correction-title"><div class="module-form-sheet__backdrop" (click)="closed.emit()"></div>
    <form class="panel panel--padded module-form" (submit)="$event.preventDefault(); confirm()">
      <div class="panel-heading"><h2 id="payment-correction-title">Corregir medio de pago · {{ sale().visibleNumber }}</h2>
        <p>Total de la venta: <strong>{{ money(sale().total) }}</strong>. El total no cambia; solo cómo se pagó.</p></div>
      <p class="field-label">Así quedó registrada</p>
      <ul class="payment-correction-current">
        @for (payment of sale().payments; track payment.id) { <li>{{ payment.methodName }}: {{ money(payment.appliedAmount) }}</li> }
      </ul>
      <p class="field-label">Así se pagó en realidad</p>
      <cp-payment-methods [drafts]="drafts" mode="payment" idPrefix="payment-correction" amountLabel="Valor por este medio" />
      <p class="form-hint">Suma: {{ money(total()) }} de {{ money(sale().total) }}. La referencia de Nequi o transferencia es opcional.</p>
      <p class="form-hint">Si la caja de esta venta ya se cerró, ese cierre no cambia: la corrección se registra en la caja abierta de hoy y queda unida a la venta.</p>
      <label class="field-label field-label--spaced" for="payment-correction-reason">Motivo</label>
      <select id="payment-correction-reason" class="text-field" name="reason" [(ngModel)]="reasonId">
        <option value="">Elige el motivo</option>
        @for (reason of reasons(); track reason.id) { <option [value]="reason.id">{{ reason.name }}</option> }
      </select>
      <label class="field-label field-label--spaced" for="payment-correction-observation">Observación {{ selectedReason?.code === 'OTRO' ? '(obligatoria)' : '(opcional)' }}</label>
      <textarea id="payment-correction-observation" class="text-field" name="observation" rows="2" maxlength="500" [(ngModel)]="observation"></textarea>
      @if (error()) { <p class="field-error" role="alert">{{ error() }}</p> }
      <div class="form-actions">
        <button class="button button--secondary" type="button" (click)="closed.emit()" [disabled]="saving()">Cancelar</button>
        <button class="button button--primary" type="submit" [disabled]="saving() || loading()">{{ saving() ? 'Guardando…' : 'Corregir medio de pago' }}</button>
      </div>
    </form></div>`,
  styles: [`
    .payment-correction-current { margin: 0 0 12px; padding-left: 18px; }
  `],
})
export class SalePaymentCorrection implements OnInit {
  private readonly api = inject(SalesApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly session = inject(SessionService);
  readonly sale = input.required<SaleDetail>();
  readonly corrected = output<CorrectedPayments>();
  readonly closed = output<void>();
  readonly reasons = signal<SaleCancellationReason[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly money = money;
  drafts: PaymentDraft[] = [];
  reasonId = '';
  observation = '';
  private pending: Pending | null = null;

  ngOnInit(): void { void this.initialize(); }

  private async initialize(): Promise<void> {
    this.allWith(this.sale().payments.some(p => p.methodCode === 'EFECTIVO') ? 'NEQUI' : 'EFECTIVO');
    try {
      this.reasons.set(await this.api.cancellationReasons());
      if (!this.reasons().length) this.error.set('No hay motivos configurados. Pide al Administrador que los cree.');
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  get selectedReason(): SaleCancellationReason | undefined { return this.reasons().find(r => r.id === this.reasonId); }
  total(): number { return appliedTotal(this.drafts); }

  /** Propone todo el total con un solo medio; en efectivo, lo recibido es el total exacto. */
  allWith(method: PaymentMethod): void {
    const total = this.sale().total;
    this.drafts.splice(0, this.drafts.length, { methodCode: method, appliedAmount: total, receivedAmount: method === 'EFECTIVO' ? total : null, reference: '' });
  }

  validation(): string {
    // The reference is optional here (as when selling), so it is not part of the shared check.
    const issue = validateDrafts(this.drafts.map(d => ({ ...d, reference: d.reference.trim() || 'opcional' })), {
      mode: 'payment', exact: this.sale().total, exactMessage: `Los medios deben sumar exactamente ${money(this.sale().total)}.`,
    });
    if (issue) return issue;
    const signature = (rows: { methodCode: string; appliedAmount: number }[]) =>
      rows.map(r => `${r.methodCode}:${Number(r.appliedAmount)}`).sort((a, b) => a.localeCompare(b)).join('|');
    if (signature(this.drafts) === signature(this.sale().payments)) return 'Es igual a como quedó registrada: cambia el medio de pago.';
    if (!this.reasonId) return 'Elige el motivo.';
    if (this.selectedReason?.code === 'OTRO' && !this.observation.trim()) return 'Escribe una observación para el motivo «Otro».';
    return '';
  }

  async confirm(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.error.set('');
    const sale = this.sale();
    await this.authorization.open({
      title: 'Autorizar corrección de medio de pago',
      description: 'La venta y su total no cambian. Se anulan los pagos registrados y se guardan los correctos; la caja esperará el efectivo nuevo.',
      detail: `${sale.visibleNumber} · total ${money(sale.total)}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
      onError: message => this.error.set(message),
      run: credentials => this.submit(sale, credentials),
    });
  }

  /** Runs inside the authorization dialog: errors are rethrown so that the dialog shows them. */
  private async submit(sale: SaleDetail, credentials: AuthorizationCredentials | null): Promise<void> {
    const body = { expectedVersion: sale.version, reasonId: this.reasonId, observation: this.observation.trim() || null, payments: toPayments(this.drafts) };
    const fingerprint = JSON.stringify({ id: sale.id, body });
    const authorizer = authorizationIdentity(credentials);
    if (!this.pending || this.pending.fingerprint !== fingerprint || this.pending.authorizerUserName !== authorizer)
      this.pending = { key: crypto.randomUUID(), fingerprint, authorizerUserName: authorizer };
    this.saving.set(true);
    try {
      const result = await this.api.correctPayments(sale.id, credentials ? { ...body, authorization: authorizationBody(credentials) } : body, this.pending.key);
      this.pending = null;
      this.corrected.emit(result);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.pending = null;
        throw new Error(reasonOr(error, credentials ? 'No se autorizó. Debe autorizar un Administrador.' : 'No tienes permiso para corregir ventas.'));
      }
      throw error;
    } finally { this.saving.set(false); }
  }
}

/** Mensaje para el usuario: cuánto efectivo de más o de menos espera ahora la caja. */
export function paymentCorrectionNotice(result: CorrectedPayments): string {
  const methods = result.payments.map(p => p.methodName).join(' y ');
  const cash = result.cashDifference === 0 ? 'El efectivo esperado en la caja no cambia.'
    : result.cashDifference < 0 ? `La caja ahora espera ${money(-result.cashDifference)} menos en efectivo.`
    : `La caja ahora espera ${money(result.cashDifference)} más en efectivo.`;
  return `Venta ${result.visibleNumber}: medio de pago corregido a ${methods}. ${cash}`;
}
