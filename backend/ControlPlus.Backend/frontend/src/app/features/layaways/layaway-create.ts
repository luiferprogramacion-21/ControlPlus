import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { Customer, CustomersApi } from '../customers/customers-api';
import { CreateLayawayInput, CreatedLayaway, LayawayCreationRules, LayawayPriceType, LayawaysApi } from './layaways-api';
import { appliedTotal, cashChange, emptyDraft, money, PaymentDraft, PaymentMethods, toPayments, validateDrafts } from './payment-methods';
import { WHOLESALE_MIN_QUANTITY } from '../sales/sale-discount';

export interface CreateLine { product: Product; quantity: number; priceType: LayawayPriceType; }

/** Same rule as the server: ceil(total x percentage / 100), at least one peso; one peso when the minimum is not required. */
export function minimumInitialPayment(total: number, rules: Pick<LayawayCreationRules, 'requireMinimumInitial' | 'minimumPercentage'> | null): number | null {
  if (!rules || !Number.isFinite(total) || total <= 0) return null;
  if (!rules.requireMinimumInitial) return 1;
  const hundredths = BigInt(Math.round(rules.minimumPercentage * 100));
  const minimum = (BigInt(Math.round(total)) * hundredths + 9999n) / 10000n;
  return Math.min(Math.round(total), Math.max(1, Number(minimum)));
}

/** Local calendar date n days from today, formatted dd/MM/yyyy (the server decides with its own operating date). */
export function dateInDays(days: number, from = new Date()): string {
  const date = new Date(from.getFullYear(), from.getMonth(), from.getDate() + days);
  return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${date.getFullYear()}`;
}
interface Pending { key: string; fingerprint: string; authorizerUserName: string | null; }

@Component({
  selector: 'cp-layaway-create',
  imports: [FormsModule, PaymentMethods],
  templateUrl: './layaway-create.html',
  styleUrl: './layaways.scss',
})
export class LayawayCreate {
  private readonly api = inject(LayawaysApi);
  private readonly customersApi = inject(CustomersApi);
  private readonly catalog = inject(CatalogApi);
  private readonly authorization = inject(AuthorizationDialog);
  readonly session = inject(SessionService);
  readonly created = output<CreatedLayaway>();
  readonly closed = output<void>();
  readonly customer = signal<Customer | null>(null);
  readonly customerMatches = signal<Customer[]>([]);
  readonly results = signal<Product[]>([]);
  readonly lines = signal<CreateLine[]>([]);
  readonly saving = signal(false);
  readonly error = signal('');
  /** Minimum initial payment rule and default term; null until loaded or if the server could not be read. */
  readonly rules = signal<LayawayCreationRules | null>(null);
  readonly total = computed(() => this.lines().reduce((sum, line) => sum + this.unitPrice(line) * Number(line.quantity || 0), 0));
  customerSearch = ''; productSearch = ''; dueDate = '';
  drafts: PaymentDraft[] = [emptyDraft()];
  readonly money = money;
  private pending: Pending | null = null;
  readonly minimumInitial = computed(() => minimumInitialPayment(this.total(), this.rules()));
  /** Default due date term and the date it would give; null when there is no default or the rules are unknown. */
  readonly defaultDue = computed(() => { const days = this.rules()?.defaultDays; return days == null ? null : { days, date: dateInDays(days) }; });

  /** Units required for the wholesale price; 12 is only the fallback if the rule cannot be read. */
  readonly wholesaleMin = signal(WHOLESALE_MIN_QUANTITY);
  constructor() { void this.loadRules(); void this.loadPricingRules(); }
  private async loadPricingRules(): Promise<void> {
    try { this.wholesaleMin.set((await this.api.pricingRules()).wholesaleMinimumQuantity); }
    catch { /* The server still enforces the minimum. */ }
  }

  private async loadRules(): Promise<void> {
    try { this.rules.set(await this.api.creationRules()); }
    catch { /* The server still enforces the rules; the screen keeps its generic texts. */ }
  }
  get dueRequired(): boolean { return this.rules()?.defaultDays === null; }
  pct(value: number): string { return new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(value); }

  get initialPayment(): number { return appliedTotal(this.drafts); }
  get change(): number { return cashChange(this.drafts); }
  /** Administrador and Supervisor create without a second authorizer; the Cajero always needs another user. */
  get selfAuthorized(): boolean { return this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR'); }
  get canFindCustomers(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }

  unitPrice(line: CreateLine): number {
    return line.priceType === 'MAYORISTA' ? line.product.wholesalePrice ?? 0 : line.product.retailPrice;
  }

  async findCustomer(): Promise<void> {
    if (!this.canFindCustomers || !this.customerSearch.trim()) return;
    try {
      this.customerMatches.set((await this.customersApi.list({ search: this.customerSearch.trim(), status: 'active', page: 1, pageSize: 20 })).items);
      this.error.set('');
    } catch (e) { this.error.set(errorText(e)); }
  }
  selectCustomer(customer: Customer): void { this.customer.set(customer); this.customerSearch = customer.fullName; this.customerMatches.set([]); }
  clearCustomer(): void { this.customer.set(null); this.customerSearch = ''; }

  async findProduct(): Promise<void> {
    const term = this.productSearch.trim(); if (!term) return;
    try { this.results.set((await this.catalog.products({ search: term, page: 1, pageSize: 30 })).items); this.error.set(''); }
    catch (e) { this.error.set(errorText(e)); }
  }
  add(product: Product, priceType: LayawayPriceType = 'MINORISTA'): void {
    if (!product.isActive || product.availableStock < 1) { this.error.set('Producto inactivo o agotado.'); return; }
    if (priceType === 'MAYORISTA' && !product.wholesalePrice) { this.error.set('Este producto no tiene precio mayorista.'); return; }
    const existing = this.lines().find(line => line.product.id === product.id);
    if (existing && existing.priceType !== priceType) { this.error.set('Este producto ya está en la lista con otro tipo de precio.'); return; }
    this.error.set('');
    this.lines.set(existing
      ? this.lines().map(line => line === existing ? { ...line, quantity: Math.min(line.quantity + 1, product.availableStock) } : line)
      : [...this.lines(), { product, quantity: 1, priceType }]);
    this.results.set([]); this.productSearch = '';
  }
  remove(line: CreateLine): void { this.lines.set(this.lines().filter(x => x !== line)); }
  update(): void { this.lines.set([...this.lines()]); }
  setPriceType(line: CreateLine, priceType: LayawayPriceType): void {
    if (priceType === 'MAYORISTA' && !line.product.wholesalePrice) { this.error.set('Este producto no tiene precio mayorista.'); this.update(); return; }
    line.priceType = priceType; this.error.set(''); this.update();
  }

  validation(): string {
    if (!this.customer()) return 'Selecciona un cliente activo.';
    if (!this.lines().length) return 'Agrega al menos un producto.';
    if (this.lines().some(l => !Number.isSafeInteger(Number(l.quantity)) || l.quantity < 1 || l.quantity > l.product.availableStock)) return 'Revisa las cantidades: deben ser enteras y no superar la existencia disponible.';
    if (this.lines().some(l => l.priceType === 'MAYORISTA' && !l.product.wholesalePrice)) return 'Hay un producto sin precio mayorista.';
    const shortWholesale = this.lines().find(l => l.priceType === 'MAYORISTA' && Number(l.quantity) < this.wholesaleMin());
    if (shortWholesale) return `El precio mayorista exige ${this.wholesaleMin()} o más unidades de ${shortWholesale.product.name}.`;
    if (this.total() <= 0) return 'El total del apartado debe ser mayor que cero.';
    const paymentIssue = validateDrafts(this.drafts, { mode: 'payment', max: this.total(), overMessage: 'El pago inicial supera el total del apartado.' });
    if (paymentIssue) return paymentIssue;
    if (this.dueRequired && !this.dueDate) return 'Elige la fecha límite del apartado: no hay un plazo predeterminado.';
    const minimum = this.minimumInitial();
    if (minimum !== null && this.initialPayment < minimum) return `El pago inicial debe ser al menos ${money(minimum)}.`;
    return '';
  }

  private input(): Omit<CreateLayawayInput, 'authorization'> {
    return {
      customerId: this.customer()!.id,
      lines: this.lines().map(l => ({ productId: l.product.id, quantity: Number(l.quantity), priceType: l.priceType })),
      payments: toPayments(this.drafts),
      dueDate: this.dueDate || null,
    };
  }

  async confirm(): Promise<void> {
    if (this.saving()) return;
    const issue = this.validation(); if (issue) { this.error.set(issue); return; }
    this.error.set('');
    if (this.selfAuthorized) {
      try { await this.submit(null); }
      catch (e) { this.error.set(e instanceof Error && !(e instanceof HttpErrorResponse) ? e.message : errorText(e)); }
      return;
    }
    await this.authorization.open({
      title: 'Autorizar apartado',
      description: 'Se registrará el apartado, se reservará el inventario y se cobrará el pago inicial en Caja.',
      detail: `${this.customer()!.fullName} · total ${money(this.total())} · pago inicial ${money(this.initialPayment)}`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      run: credentials => this.submit(credentials),
    });
  }

  /** Runs inside the authorization dialog: errors are rethrown so that the dialog shows them. */
  private async submit(credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.saving()) return;
    const body = this.input();
    const fingerprint = JSON.stringify(body);
    const authorizer = authorizationIdentity(credentials);
    if (this.pending && this.pending.fingerprint === fingerprint && this.pending.authorizerUserName !== authorizer)
      throw new Error('Reintenta con el mismo usuario autorizador; la clave de este apartado conserva su identidad.');
    if (!this.pending || this.pending.fingerprint !== fingerprint) this.pending = { key: crypto.randomUUID(), fingerprint, authorizerUserName: authorizer };
    this.saving.set(true);
    try {
      const request: CreateLayawayInput = credentials ? { ...body, authorization: authorizationBody(credentials) } : body;
      const result = await this.api.create(request, this.pending.key);
      this.pending = null;
      this.created.emit(result);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 403) {
        this.pending = null;
        if (!credentials) throw new Error('No tienes permiso para crear apartados.');
        throw new Error('No se autorizó el apartado. Verifica el rol, el permiso y las credenciales del autorizador.');
      }
      throw error;
    } finally { this.saving.set(false); }
  }
}
