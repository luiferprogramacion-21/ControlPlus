import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { toastSignal } from '../../core/toast';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { NumberInput } from '../../shared/number-input';
import { WindowSheet } from '../../shared/window-sheet';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { LayawayDetail, LayawayProductCorrection, LayawaysApi } from './layaways-api';
import { appliedTotal, emptyDraft, money, PaymentDraft, PaymentMethods, toRefunds, validateDrafts } from './payment-methods';

/** Una línea del apartado tal como quedará; `original` es la cantidad que tenía (0 si se agrega ahora). */
export interface ProductRow {
  productId: string; code: string; name: string; unitPrice: number; quantity: number; original: number; available: number | null;
}

/**
 * Corregir productos de un apartado sin entregar (decisión del 19 de noviembre de 2026): cambiar cantidades, quitar
 * (cantidad 0) o agregar productos. El servidor ajusta reservas y saldo y, si lo abonado supera el total nuevo, registra la
 * devolución por los medios elegidos. Un Supervisor necesita la autorización de un Administrador.
 */
@Component({
  selector: 'cp-layaway-products',
  imports: [WindowSheet, NumberInput, FormsModule, PaymentMethods],
  templateUrl: './layaway-products.html',
  styleUrl: './layaways.scss',
})
export class LayawayProducts implements OnInit {
  private readonly api = inject(LayawaysApi);
  private readonly catalog = inject(CatalogApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly session = inject(SessionService);
  readonly detail = input.required<LayawayDetail>();
  readonly corrected = output<LayawayProductCorrection>();
  readonly closed = output<void>();
  readonly saving = signal(false);
  readonly searching = signal(false);
  readonly error = toastSignal('error');
  readonly money = money;
  readonly rows = signal<ProductRow[]>([]);
  readonly results = signal<Product[]>([]);
  search = '';
  reason = '';
  drafts: PaymentDraft[] = [emptyDraft()];
  private pending: { key: string; fingerprint: string; authorizer: string | null } | null = null;

  ngOnInit(): void {
    this.rows.set(this.detail().lines.map(line => ({ productId: line.productId, code: line.productCode, name: line.productName,
      unitPrice: line.unitPrice, quantity: line.quantity, original: line.quantity, available: null })));
  }

  get paid(): number { const layaway = this.detail().layaway; return layaway.total - layaway.balance; }
  get newTotal(): number { return this.rows().reduce((sum, row) => sum + row.unitPrice * (Number(row.quantity) || 0), 0); }
  get refund(): number { return Math.max(0, this.paid - this.newTotal); }
  get newBalance(): number { return Math.max(0, this.newTotal - this.paid); }
  get refundTotal(): number { return appliedTotal(this.drafts); }
  get changed(): boolean { return this.rows().some(row => (Number(row.quantity) || 0) !== row.original); }

  /** − y + en la fila; 0 quita el producto. Un producto nuevo no supera lo disponible. */
  step(row: ProductRow, delta: number): void {
    const next = Math.max(0, (Number(row.quantity) || 0) + delta);
    row.quantity = row.available !== null ? Math.min(next, row.available) : next;
    this.sync();
  }

  /** Recalcula después de cambiar una cantidad. Con un solo medio de devolución, su importe es la devolución completa. */
  sync(): void {
    this.rows.set([...this.rows()]);
    if (this.drafts.length === 1) this.drafts[0].appliedAmount = this.refund;
  }

  async searchProducts(): Promise<void> {
    const term = this.search.trim();
    if (!term || this.searching()) return;
    this.searching.set(true); this.error.set('');
    try {
      const page = await this.catalog.products({ search: term, page: 1, pageSize: 8 });
      this.results.set(page.items);
      if (!page.items.length) this.error.set('No se encontraron productos con esa búsqueda.');
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.searching.set(false); }
  }

  add(product: Product): void {
    if (!product.isActive || product.availableStock < 1) return;
    const existing = this.rows().find(row => row.productId === product.id);
    if (existing) { this.step(existing, 1); }
    else this.rows.set([...this.rows(), { productId: product.id, code: product.internalCode, name: product.name,
      unitPrice: product.retailPrice, quantity: 1, original: 0, available: product.availableStock }]);
    this.sync();
    this.results.set([]); this.search = '';
  }

  validation(): string {
    if (this.rows().some(row => !Number.isSafeInteger(Number(row.quantity)) || Number(row.quantity) < 0))
      return 'Las cantidades deben ser enteros de 0 en adelante.';
    if (this.rows().some(row => row.available !== null && Number(row.quantity) > row.available))
      return 'Un producto nuevo supera las unidades disponibles.';
    if (!this.changed) return 'No cambiaste ningún producto ni cantidad.';
    if (this.newTotal <= 0) return 'El apartado debe conservar al menos un producto. Para quitarlo todo usa «Cancelar apartado».';
    if (!this.reason.trim()) return 'Escribe el motivo de la corrección.';
    if (this.reason.trim().length > 500) return 'El motivo admite hasta 500 caracteres.';
    if (this.refund > 0) {
      if (!this.session.has('LAYAWAY.CANCEL') || !this.session.has('LAYAWAY.REFUND_DECIDE'))
        return 'Esta corrección devuelve dinero: la debe hacer quien puede cancelar apartados y decidir devoluciones.';
      return validateDrafts(this.drafts, { mode: 'refund', exact: this.refund, exactMessage: 'Los medios deben sumar exactamente la devolución.' });
    }
    return '';
  }

  private body() {
    return {
      expectedVersion: this.detail().layaway.version, reason: this.reason.trim(),
      lines: this.rows().filter(row => Number(row.quantity) > 0).map(row => ({ productId: row.productId, quantity: Number(row.quantity) })),
      refunds: this.refund > 0 ? toRefunds(this.drafts) : [],
    };
  }

  async confirm(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.error.set('');
    const layaway = this.detail().layaway;
    await this.authorization.open({
      title: 'Autorizar corrección de productos',
      description: 'Se ajustan las reservas y el saldo del apartado' + (this.refund > 0 ? ' y se devuelve el dinero sobrante por los medios elegidos.' : '.'),
      detail: `${layaway.number} · total ${money(layaway.total)} → ${money(this.newTotal)}` + (this.refund > 0 ? ` · devolver ${money(this.refund)}` : ''),
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message),
      run: credentials => this.execute(credentials),
    });
  }

  private async execute(credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.saving()) return;
    const body = this.body();
    const fingerprint = JSON.stringify({ id: this.detail().layaway.id, body });
    const authorizer = authorizationIdentity(credentials);
    if (this.pending?.fingerprint === fingerprint && this.pending.authorizer !== authorizer)
      throw new Error('Reintenta con el mismo Administrador autorizador.');
    if (this.pending?.fingerprint !== fingerprint) this.pending = { fingerprint, authorizer, key: crypto.randomUUID() };
    this.saving.set(true);
    try {
      const result = await this.api.correctProducts(this.detail().layaway,
        credentials ? { ...body, authorization: authorizationBody(credentials) } : body, this.pending.key);
      this.pending = null;
      this.corrected.emit(result);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.pending = null;
        throw new Error('No se autorizó la corrección. La autoriza un Administrador vigente; la devolución requiere permiso para cancelar apartados.');
      }
      throw error;
    } finally { this.saving.set(false); }
  }
}
