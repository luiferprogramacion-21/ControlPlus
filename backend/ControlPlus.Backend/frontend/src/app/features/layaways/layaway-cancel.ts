import { NumberInput } from '../../shared/number-input';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { reasonOr } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { LayawayCancellation, LayawayDetail, LayawaysApi } from './layaways-api';
import { appliedTotal, emptyDraft, money, PaymentDraft, PaymentMethods, toRefunds, validateDrafts } from './payment-methods';
import { toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';

interface Pending { key: string; fingerprint: string; authorizerUserName: string | null; }

@Component({
  selector: 'cp-layaway-cancel',
  imports: [WindowSheet, NumberInput, FormsModule, PaymentMethods],
  templateUrl: './layaway-cancel.html',
  styleUrl: './layaways.scss',
})
export class LayawayCancel implements OnInit {
  private readonly api = inject(LayawaysApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly session = inject(SessionService);
  readonly detail = input.required<LayawayDetail>();
  readonly cancelled = output<LayawayCancellation>();
  readonly closed = output<void>();
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly money = money;
  refundAmount = 0;
  observation = '';
  drafts: PaymentDraft[] = [emptyDraft()];
  private pending: Pending | null = null;

  /** Refunds are limited to what was actually applied; reversed payments do not count. */
  readonly totalPaid = computed(() => this.detail().payments.filter(p => p.state === 'APLICADO').reduce((sum, p) => sum + p.appliedAmount, 0));
  get retained(): number { return this.totalPaid() - Number(this.refundAmount || 0); }
  get refundTotal(): number { return appliedTotal(this.drafts); }

  /** Auditoría del 23/11/2026: se propone devolver todo lo abonado; con un solo medio, ese medio lleva el valor completo. */
  ngOnInit(): void { this.refundAmount = this.totalPaid(); }
  private syncSingleMethod(): void { if (this.drafts.length === 1) this.drafts[0].appliedAmount = Number(this.refundAmount || 0); }

  validation(): string {
    this.syncSingleMethod();
    const amount = Number(this.refundAmount);
    if (!Number.isSafeInteger(amount) || amount < 0) return 'El valor a devolver debe ser un entero no negativo.';
    if (amount > this.totalPaid()) return 'El valor a devolver no puede superar lo abonado.';
    if (this.observation.trim().length > 1000) return 'La observación admite hasta 1.000 caracteres.';
    if (amount === 0) return '';
    return validateDrafts(this.drafts, { mode: 'refund', exact: amount, exactMessage: 'Los medios deben sumar exactamente el valor a devolver.' });
  }

  private body() {
    this.syncSingleMethod();
    const amount = Number(this.refundAmount);
    return { refundAmount: amount, refunds: amount > 0 ? toRefunds(this.drafts) : [], reasonId: null, observation: this.observation.trim() || null };
  }

  async confirm(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.error.set('');
    const layaway = this.detail().layaway;
    await this.authorization.open({
      title: 'Autorizar cancelación de apartado',
      description: 'Se cancelará el apartado, se liberará la reserva de inventario y se registrará la devolución indicada. La existencia real no cambia.',
      detail: `${layaway.number} · abonado ${money(this.totalPaid())} · devolver ${money(Number(this.refundAmount))} · retenido ${money(this.retained)}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'], operatorScoped: true,
      onError: message => this.error.set(message),
      run: credentials => this.submit(credentials),
    });
  }

  /** Runs inside the authorization dialog: errors are rethrown so that the dialog shows them. */
  private async submit(credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.saving()) return;
    const layaway = this.detail().layaway;
    const body = this.body();
    const fingerprint = JSON.stringify({ id: layaway.id, version: layaway.version, body });
    const authorizer = authorizationIdentity(credentials);
    if (this.pending && this.pending.fingerprint === fingerprint && this.pending.authorizerUserName !== authorizer)
      throw new Error('Reintenta con el mismo usuario autorizador; la clave de esta cancelación conserva su identidad.');
    if (!this.pending || this.pending.fingerprint !== fingerprint) this.pending = { key: crypto.randomUUID(), fingerprint, authorizerUserName: authorizer };
    this.saving.set(true);
    try {
      const result = await this.api.cancel(layaway, credentials ? { ...body, authorization: authorizationBody(credentials) } : body, this.pending.key);
      this.pending = null;
      this.cancelled.emit(result);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.pending = null;
        throw new Error(reasonOr(error, credentials ? 'No se autorizó la cancelación. Verifica el rol, el permiso y las credenciales del autorizador.' : 'No tienes permiso para cancelar apartados ni decidir devoluciones.'));
      }
      throw error;
    } finally { this.saving.set(false); }
  }
}
