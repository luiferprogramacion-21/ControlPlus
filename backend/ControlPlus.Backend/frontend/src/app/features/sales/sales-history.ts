import { A11yModule } from '@angular/cdk/a11y';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { CancelSaleInput, ConfirmedChange, printJobStatusText, SaleCancellationReason, SaleDetail, SalesApi, SaleSummary } from './sales-api';
import { SaleChange } from './sale-change';
import { SalePrinting } from './sale-printing';
import { statusKind, toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';

const STATE_LABELS: Record<string, string> = { CONFIRMADA: 'Confirmada', ANULADA: 'Anulada' };
interface Pending { key: string; fingerprint: string; authorizerUserName: string | null; }

@Component({
  selector: 'cp-sales-history',
  imports: [WindowSheet, A11yModule, CurrencyPipe, DatePipe, DecimalPipe, FormsModule, MatPaginatorModule, RouterLink, Icon, SaleChange],
  templateUrl: './sales-history.html',
  styleUrls: ['../directory.scss', './sales-history.scss'],
})
export class SalesHistory implements OnInit {
  /** Cómo se pagó: «Crédito» o los medios usados en contado (por ejemplo «Efectivo + Nequi»). */
  paymentLabel(sale: { saleType?: string; paymentMethods?: string[] | null }): string {
    if (sale.saleType === 'CREDITO') return 'Crédito';
    return sale.paymentMethods?.length ? sale.paymentMethods.join(' + ') : '—';
  }
  private readonly api = inject(SalesApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly printing = inject(SalePrinting);
  readonly session = inject(SessionService);
  readonly result = signal<Page<SaleSummary> | null>(null);
  readonly detail = signal<SaleDetail | null>(null);
  readonly reasons = signal<SaleCancellationReason[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly cancelling = signal(false);
  readonly changing = signal(false);
  readonly printMessage = toastSignal(statusKind);
  readonly printStatus = signal<string | null>(null);
  readonly reprinting = signal(false);
  readonly printingId = signal<string | null>(null);
  private printKey: { saleId: string; key: string } | null = null;
  readonly error = toastSignal('error');
  readonly formError = toastSignal('error');
  readonly notice = toastSignal('success');
  page = 1; pageSize = 20;
  reasonId = ''; observation = '';
  private requestId = 0;
  private pending: Pending | null = null;
  private readonly route = inject(ActivatedRoute, { optional: true });
  /** Cambios y devoluciones (decisión del 19 de noviembre de 2026): se busca la venta por su número y se abre el cambio. */
  readonly findingChange = signal(false);
  readonly findingBusy = signal(false);
  readonly findError = signal('');
  changeNumber = '';

  ngOnInit(): void {
    void this.reload();
    const query = this.route?.snapshot.queryParamMap;
    if (query?.get('accion') === 'cambio' && this.session.has('SALES.CREATE')) this.startChangeSearch(query.get('venta') ?? '');
  }
  startChangeSearch(visibleNumber = ''): void {
    this.changeNumber = visibleNumber; this.findError.set(''); this.findingChange.set(true);
  }
  async findChangeSale(): Promise<void> {
    const visibleNumber = this.changeNumber.trim();
    if (!visibleNumber || this.findingBusy()) return;
    this.findingBusy.set(true); this.findError.set('');
    try {
      const preparation = await this.api.findChangeSale(visibleNumber);
      const detail = await this.api.detail(preparation.saleId);
      if (!this.canChange(detail)) { this.findError.set(detail.state === 'ANULADA' ? 'Esa venta está anulada; no admite cambios.' : 'Esa venta no admite cambios.'); return; }
      this.detail.set(detail); this.findingChange.set(false); this.changing.set(true);
    } catch (e) {
      this.findError.set(e instanceof HttpErrorResponse && e.status === 404 ? `No se encontró la venta ${visibleNumber}. Revisa el número del ticket.` : errorText(e));
    } finally { this.findingBusy.set(false); }
  }
  label(state: string): string { return STATE_LABELS[state] ?? state; }
  get listsAll(): boolean { return this.session.has('SALES.ALL_READ'); }
  /** Annulment is for Supervisor and Administrador with the effective permission; the server remains the authority. */
  canCancel(sale: SaleDetail): boolean { return sale.state === 'CONFIRMADA' && this.session.has('SALES.VOID') && this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR'); }
  /** Any seller may start a change; a Cajero needs a higher-role authorizer. The server remains the authority. */
  canChange(sale: SaleDetail): boolean { return sale.state === 'CONFIRMADA' && this.session.has('SALES.CREATE'); }
  async changeDone(change: ConfirmedChange): Promise<void> {
    this.changing.set(false);
    this.notice.set(`Cambio ${change.visibleNumber} registrado. Diferencia ${this.money(change.difference)}.`);
    const id = this.detail()?.id;
    try { if (id) this.detail.set(await this.api.detail(id)); } catch { this.detail.set(null); }
    await this.reload();
  }
  canPrint(sale: SaleSummary): boolean { return sale.state === 'CONFIRMADA' && this.session.has('SALES.REPRINT'); }
  /** A failed request keeps its Idempotency-Key so a retry never duplicates the request; it rotates after a success. */
  async print(sale: SaleSummary): Promise<void> {
    if (this.printingId()) return;
    if (this.printKey?.saleId !== sale.id) this.printKey = { saleId: sale.id, key: crypto.randomUUID() };
    this.printingId.set(sale.id); this.error.set(''); this.notice.set('');
    try {
      await this.api.requestReprint(sale.id, this.printKey.key);
      this.printKey = null;
      const jobs = (await this.api.printJobs(sale.id)).items;
      const latest = [...jobs].sort((a, b) => b.requestedAtUtc.localeCompare(a.requestedAtUtc))[0];
      this.notice.set(latest
        ? `Impresión de ${sale.visibleNumber}: ${printJobStatusText(latest)}${latest.status === 'IMPRESO' ? ' Esto no confirma que haya salido el papel.' : ''}`
        : `Impresión de ${sale.visibleNumber} solicitada, pero aún no hay trabajo en cola. Verifica que exista un perfil de impresora configurado.`);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.printingId.set(null); }
  }
  get selectedReason(): SaleCancellationReason | undefined { return this.reasons().find(reason => reason.id === this.reasonId); }

  async reload(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true); this.error.set('');
    try {
      const result = await this.api.list(this.listsAll, this.page, this.pageSize);
      if (id === this.requestId) this.result.set(result);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }
  pageChanged(event: PageEvent): void { this.page = event.pageIndex + 1; this.pageSize = event.pageSize; void this.reload(); }

  async open(sale: SaleSummary): Promise<void> {
    this.notice.set(''); this.error.set('');
    this.printMessage.set(''); this.printStatus.set(null);
    try { this.detail.set(await this.api.detail(sale.id)); } catch (e) { this.error.set(errorText(e)); return; }
    this.printStatus.set(await this.printing.lastStatus(sale.id));
  }
  /** Reprinting needs the effective permission and a confirmed sale; it is always recorded. */
  canReprint(sale: SaleDetail): boolean { return sale.state === 'CONFIRMADA' && this.session.has('SALES.REPRINT'); }
  async reprint(): Promise<void> {
    const sale = this.detail(); if (!sale || this.reprinting()) return;
    this.reprinting.set(true); this.printMessage.set('');
    const outcome = await this.printing.reprint(sale.id);
    this.printMessage.set(outcome.message); if (outcome.status) this.printStatus.set(outcome.status);
    this.reprinting.set(false);
  }
  closeDetail(): void { this.detail.set(null); this.cancelling.set(false); this.pending = null; }

  async startCancel(): Promise<void> {
    this.formError.set(''); this.reasonId = ''; this.observation = ''; this.pending = null;
    try {
      this.reasons.set(await this.api.cancellationReasons());
      if (!this.reasons().length) { this.error.set('No hay motivos de anulación configurados. Pide al Administrador que los inicialice.'); return; }
      this.cancelling.set(true);
    } catch (e) { this.error.set(errorText(e)); }
  }

  validation(): string {
    if (!this.reasonId) return 'Selecciona el motivo de la anulación.';
    if (this.selectedReason?.code === 'OTRO' && !this.observation.trim()) return 'La observación es obligatoria cuando el motivo es «Otro».';
    if (this.observation.trim().length > 500) return 'La observación admite hasta 500 caracteres.';
    return '';
  }

  async confirm(): Promise<void> {
    if (this.saving()) return;
    const sale = this.detail(); if (!sale) return;
    const issue = this.validation(); if (issue) { this.formError.set(issue); return; }
    this.formError.set('');
    await this.authorization.open({
      title: 'Autorizar anulación de venta',
      description: 'Se anulará la venta, se repondrá el inventario y se revertirán sus pagos o el crédito pendiente. Esta acción no se puede deshacer.',
      detail: `${sale.visibleNumber} · total ${this.money(sale.total)}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
      onError: message => this.formError.set(message),
      run: credentials => this.submit(sale, credentials),
    });
  }

  /** Runs inside the authorization dialog: errors are rethrown so that the dialog shows them. */
  private async submit(sale: SaleDetail, credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.saving()) return;
    const body: CancelSaleInput = { expectedVersion: sale.version, reasonId: this.reasonId, observation: this.observation.trim() || null };
    const fingerprint = JSON.stringify({ id: sale.id, body });
    const authorizer = authorizationIdentity(credentials);
    if (this.pending && this.pending.fingerprint === fingerprint && this.pending.authorizerUserName !== authorizer)
      throw new Error('Reintenta con el mismo usuario autorizador; la clave de esta anulación conserva su identidad.');
    if (!this.pending || this.pending.fingerprint !== fingerprint) this.pending = { key: crypto.randomUUID(), fingerprint, authorizerUserName: authorizer };
    this.saving.set(true);
    try {
      const result = await this.api.cancel(sale.id, credentials ? { ...body, authorization: authorizationBody(credentials) } : body, this.pending.key);
      this.pending = null;
      this.cancelling.set(false); this.detail.set(null);
      this.notice.set(`Venta ${result.visibleNumber} anulada. Se revirtieron ${this.money(result.reversedAmount)}.`);
      await this.reload();
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.pending = null;
        throw new Error(credentials ? 'No se autorizó la anulación. El autorizador debe tener un rol superior al tuyo y el permiso de anular ventas.' : 'No tienes permiso para anular ventas.');
      }
      throw error;
    } finally { this.saving.set(false); }
  }

  money(value: number): string { return new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value); }
}
