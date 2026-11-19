import { groupedNumber, NumberInput } from '../../shared/number-input';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { appliedTotal, emptyDraft, money, PaymentDraft, PaymentMethods, validateDrafts } from '../layaways/payment-methods';
import { ChangePaymentInput, ChangePreparation, ConfirmChangeInput, ConfirmedChange, SaleDetail, SalesApi } from './sales-api';
import { ChangeTreatment, returnValue, settle } from './sale-change-math';
import { WHOLESALE_MIN_QUANTITY } from './sale-discount';
import { toastSignal } from '../../core/toast';

interface Returning { saleLineId: string; productId: string; code: string; name: string; sold: number; remaining: number; net: number; quantity: number; resellable: boolean; }
interface Delivering { product: Product; quantity: number; priceType: 'MINORISTA' | 'MAYORISTA'; }
interface Pending { key: string; fingerprint: string; authorizerUserName: string | null; }

@Component({
  selector: 'cp-sale-change',
  imports: [NumberInput, DecimalPipe, CurrencyPipe, FormsModule, PaymentMethods],
  templateUrl: './sale-change.html',
  styleUrls: ['./sale-change.scss'],
})
export class SaleChange implements OnInit {
  private readonly api = inject(SalesApi);
  private readonly catalog = inject(CatalogApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly session = inject(SessionService);
  readonly sale = input.required<SaleDetail>();
  readonly changed = output<ConfirmedChange>();
  readonly closed = output<void>();
  readonly prep = signal<ChangePreparation | null>(null);
  readonly returning = signal<Returning[]>([]);
  readonly delivering = signal<Delivering[]>([]);
  readonly results = signal<Product[]>([]);
  readonly loading = signal(true);
  readonly searching = signal(false);
  readonly saving = signal(false);
  readonly error = toastSignal('error');
  readonly money = money;
  search = '';
  choice: ChangeTreatment | '' = '';
  drafts: PaymentDraft[] = [emptyDraft()];
  private pending: Pending | null = null;

  /** Units of a delivered product required for the wholesale price; 12 is only the fallback. */
  readonly wholesaleMin = signal(WHOLESALE_MIN_QUANTITY);
  ngOnInit(): void { void this.load(); void this.loadPricingRules(); }
  /** Igual que el punto de venta: por debajo del mínimo, la línea vuelve a precio minorista. */
  keepWholesaleMinimum(line: Delivering): void {
    if (line.priceType !== 'MAYORISTA' || Number(line.quantity) >= this.wholesaleMin()) return;
    line.priceType = 'MINORISTA';
    this.error.set(`${line.product.name}: volvió a precio minorista, el mayorista exige entregar ${this.wholesaleMin()} o más unidades.`);
  }
  private async loadPricingRules(): Promise<void> {
    try { this.wholesaleMin.set((await this.api.pricingRules()).wholesaleMinimumQuantity); }
    catch { /* The server still enforces the minimum. */ }
  }

  /** `keep` conserva lo que el usuario ya marcó (ajustado a lo que sigue disponible) al refrescar por un conflicto de versión. */
  async load(keep = false): Promise<void> {
    const previous = keep ? new Map(this.returning().map(line => [line.saleLineId, line])) : null;
    this.loading.set(true); this.error.set('');
    try {
      const prep = await this.api.changePreparation(this.sale().id);
      this.prep.set(prep);
      this.returning.set(prep.lines.filter(line => line.remainingQuantity > 0).map(line => {
        const original = this.sale().lines.find(l => l.id === line.saleLineId);
        const before = previous?.get(line.saleLineId);
        return { saleLineId: line.saleLineId, productId: line.productId, code: line.productCode, name: line.productName, sold: line.soldQuantity,
          remaining: line.remainingQuantity, net: original?.netAmount ?? 0,
          quantity: before ? Math.min(Number(before.quantity) || 0, line.remainingQuantity) : 0, resellable: before?.resellable ?? true };
      }));
      if (keep) this.error.set('La venta cambió mientras preparabas el cambio. Se actualizaron los datos; revisa los valores y confirma de nuevo.');
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }

  // --- productos que devuelve el cliente: solo se devuelve lo que se compró -------------------
  /** Marca o desmarca una línea; al marcarla se toma todo lo disponible de esa línea. */
  toggleReturn(line: Returning, selected: boolean): void { line.quantity = selected ? line.remaining : 0; }
  /** Cantidad de una línea con varias unidades: entero entre 1 y lo disponible, nunca más de lo comprado. */
  setReturnQuantity(line: Returning, value: unknown): void {
    const parsed = Math.trunc(Number(value));
    line.quantity = Number.isFinite(parsed) ? Math.min(Math.max(parsed, 1), line.remaining) : 1;
  }
  /** Medios y valores con los que se pagó la venta original (una devolución solo sale por ellos). */
  get originalPayments(): string {
    return this.sale().payments.map(payment => `${payment.methodName} ${money(payment.appliedAmount)}`).join(' · ');
  }

  // --- amounts -------------------------------------------------------------------------------
  returnedValue(line: Returning): number {
    const prep = this.prep(); const row = prep?.lines.find(l => l.saleLineId === line.saleLineId);
    return line.quantity > 0 && row ? returnValue(line.net, line.sold, row.returnedQuantity, line.quantity) : 0;
  }
  get totalReturned(): number { return this.returning().reduce((sum, line) => sum + this.returnedValue(line), 0); }
  unitPrice(line: Delivering): number { return line.priceType === 'MAYORISTA' ? (line.product.wholesalePrice ?? line.product.retailPrice) : line.product.retailPrice; }
  get totalDelivered(): number { return this.delivering().reduce((sum, line) => sum + this.unitPrice(line) * line.quantity, 0); }
  get difference(): number { return this.totalDelivered - this.totalReturned; }
  get settlement() { const prep = this.prep(); return settle(prep?.saleType ?? 'CONTADO', this.difference, prep?.creditBalance ?? null, this.choice); }
  get creditResult(): number | null { const balance = this.prep()?.creditBalance; return balance === null || balance === undefined ? null : balance + this.settlement.creditAdjustment; }

  // --- editing -------------------------------------------------------------------------------
  async find(): Promise<void> {
    if (!this.search.trim()) return;
    this.searching.set(true); this.error.set('');
    try { this.results.set((await this.catalog.products({ search: this.search.trim(), page: 1, pageSize: 20 })).items.filter(p => p.isActive)); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.searching.set(false); }
  }
  addDelivered(product: Product): void {
    if (this.saving()) return;
    const existing = this.delivering().find(line => line.product.id === product.id && line.priceType === 'MINORISTA');
    if (existing) existing.quantity += 1; else this.delivering.update(lines => [...lines, { product, quantity: 1, priceType: 'MINORISTA' }]);
    this.delivering.update(lines => [...lines]); this.results.set([]); this.search = '';
  }
  removeDelivered(line: Delivering): void { this.delivering.update(lines => lines.filter(l => l !== line)); }

  /** Stock the new product may use: what is available plus the units of the same product coming back resellable in this change. */
  private reachable(product: Product): number {
    const coming = this.returning().filter(l => l.productId === product.id && l.resellable).reduce((sum, l) => sum + (l.quantity || 0), 0);
    return product.availableStock + coming;
  }

  validation(): string {
    const prep = this.prep(); if (!prep) return 'La venta no está lista para un cambio.';
    if (!prep.canStartChange) return prep.withinDeadline ? 'Esta venta no admite cambios.' : `El plazo para cambios venció el ${prep.changeDeadline}.`;
    const returned = this.returning().filter(line => Number(line.quantity) > 0);
    if (!returned.length) return 'Indica al menos un producto devuelto.';
    for (const line of returned) if (!Number.isInteger(Number(line.quantity)) || Number(line.quantity) > line.remaining) return `La cantidad devuelta de ${line.name} debe ser un entero hasta ${groupedNumber(line.remaining)}.`;
    if (!this.delivering().length) return 'Agrega al menos un producto entregado.';
    for (const line of this.delivering()) {
      if (!Number.isInteger(Number(line.quantity)) || Number(line.quantity) < 1) return `La cantidad de ${line.product.name} debe ser un entero mayor que cero.`;
      if (Number(line.quantity) > this.reachable(line.product)) return `No hay existencias suficientes de ${line.product.name}.`;
      if (line.priceType === 'MAYORISTA' && Number(line.quantity) < this.wholesaleMin()) return `El precio mayorista exige entregar ${this.wholesaleMin()} o más unidades de ${line.product.name}.`;
    }
    const s = this.settlement; if (s.error) return s.error;
    if (s.mode === 'none') return '';
    return validateDrafts(this.drafts, { mode: s.mode === 'payment' ? 'payment' : 'refund', exact: s.cashAmount, exactMessage: `Los medios deben sumar exactamente ${money(s.cashAmount)}.` });
  }

  private body(): ConfirmChangeInput {
    const prep = this.prep()!; const s = this.settlement;
    const payments: ChangePaymentInput[] = s.mode === 'none' ? [] : this.drafts.map(d => ({
      methodCode: d.methodCode, amount: Number(d.appliedAmount),
      receivedAmount: s.mode === 'payment' && d.methodCode === 'EFECTIVO' ? Number(d.receivedAmount) : null,
      reference: d.methodCode === 'EFECTIVO' ? null : d.reference.trim(),
    }));
    return {
      expectedSaleVersion: prep.saleVersion, expectedCreditVersion: prep.creditVersion,
      returned: this.returning().filter(l => Number(l.quantity) > 0).map(l => ({ saleLineId: l.saleLineId, quantity: Number(l.quantity), resellable: l.resellable })),
      delivered: this.delivering().map(l => ({ productId: l.product.id, quantity: Number(l.quantity), priceType: l.priceType })),
      treatment: s.treatment, payments,
    };
  }

  async confirm(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.error.set('');
    const prep = this.prep()!; const s = this.settlement;
    await this.authorization.open({
      title: 'Autorizar cambio de productos',
      description: 'Los productos devueltos vuelven al inventario, los entregados salen y la diferencia se liquida. La venta original no cambia. Esta acción no se puede deshacer.',
      detail: `${prep.visibleNumber} · devuelto ${money(this.totalReturned)} · entregado ${money(this.totalDelivered)} · diferencia ${money(this.difference)}${s.creditAdjustment ? ` · crédito ${money(s.creditAdjustment)}` : ''}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
      onError: message => this.error.set(message),
      run: credentials => this.submit(credentials),
    });
  }

  /** Runs inside the authorization dialog: errors are rethrown so that the dialog shows them. */
  private async submit(credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.saving()) return;
    const sale = this.sale(); const body = this.body();
    const fingerprint = JSON.stringify({ id: sale.id, body });
    const authorizer = authorizationIdentity(credentials);
    if (this.pending && this.pending.fingerprint === fingerprint && this.pending.authorizerUserName !== authorizer)
      throw new Error('Reintenta con el mismo usuario autorizador; la clave de este cambio conserva su identidad.');
    if (!this.pending || this.pending.fingerprint !== fingerprint) this.pending = { key: crypto.randomUUID(), fingerprint, authorizerUserName: authorizer };
    this.saving.set(true);
    try {
      const result = await this.api.confirmChange(sale.id, credentials ? { ...body, authorization: authorizationBody(credentials) } : body, this.pending.key);
      this.pending = null; this.changed.emit(result);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.pending = null;
        throw new Error(credentials ? 'No se autorizó el cambio. El autorizador debe tener un rol superior al tuyo y el permiso de autorizar cambios.' : 'No tienes permiso para autorizar cambios de productos.');
      }
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.pending = null;
        // Solo un conflicto de versión justifica refrescar; las demás reglas (por ejemplo, el efectivo de la caja) se explican
        // y el formulario se conserva para corregir y volver a intentar.
        if (error.error?.code === 'concurrency.conflict') void this.load(true);
      }
      throw error;
    } finally { this.saving.set(false); }
  }

  get appliedTotal(): number { return appliedTotal(this.drafts); }
}
