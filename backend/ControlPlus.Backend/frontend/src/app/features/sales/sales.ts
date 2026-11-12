import { Component, ElementRef, HostListener, inject, OnDestroy, OnInit, signal, ViewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { CashApi, CashState } from '../cash/cash-api';
import { Sale, SaleInput, SalesApi, SalePaymentInput, PrintJobStatus } from './sales-api';
import { Customer } from '../customers/customers-api';
import { CustomerPicker } from '../customers/customer-picker';
import { buildTicketHtml, TicketPrinter } from './ticket';
import { describeDiscount, Discount, discountAmount, maxDiscountPercent, validateDiscount, WHOLESALE_MIN_QUANTITY } from './sale-discount';
import { DiscountApplied, DiscountDialog } from './discount-dialog';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';

interface CartLine { product: Product; quantity: number; priceType: 'MINORISTA' | 'MAYORISTA'; discount: Discount | null; }
interface ReceiptMeta { customer: string; operator: string; items: number; units: number; }
type PaymentCode = 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI';
const SUGGEST_DELAY_MS = 250;

@Component({ selector: 'cp-sales', imports: [FormsModule, CurrencyPipe, DatePipe, MatButtonModule, RouterLink, CustomerPicker, DiscountDialog], templateUrl: './sales.html', styleUrl: './sales.scss' })
export class Sales implements OnInit, OnDestroy {
  private readonly catalog = inject(CatalogApi);
  private readonly cash = inject(CashApi);
  private readonly api = inject(SalesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly printer = inject(TicketPrinter);
  readonly session = inject(SessionService);
  readonly cashState = signal<CashState | null>(null);
  readonly results = signal<Product[]>([]);
  readonly activeIndex = signal(-1);
  readonly cart = signal<CartLine[]>([]);
  readonly sale = signal<Sale | null>(null);
  readonly receipt = signal<ReceiptMeta | null>(null);
  readonly showDetail = signal(false);
  readonly reprintMessage = signal('');
  readonly lastJobStatus = signal<PrintJobStatus | null>(null);
  /** Whether the ticket was already sent to the printer; later prints are recorded as reprints. */
  readonly printed = signal(false);
  /** Prints the ticket by itself when the sale is confirmed; the cashier can switch it off if the printer is unavailable. */
  readonly autoPrint = signal(Sales.loadAutoPrint());
  readonly showCancel = signal(false);
  readonly showCustomer = signal(false);
  readonly checkoutMode = signal(false);
  readonly saleType = signal<'CONTADO' | 'CREDITO'>('CONTADO');
  readonly activePayments = signal<PaymentCode[]>(['EFECTIVO']);
  /** Which discount window is open: for the selected product or for the whole sale. */
  readonly discountScope = signal<'product' | 'sale' | null>(null);
  readonly selectedProductId = signal<string | null>(null);
  readonly customers = signal<Customer[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly feedback = signal('');
  readonly feedbackKind = signal<'success' | 'info' | 'error'>('info');
  search = '';
  customerId = '';
  discountReason = '';
  cashApplied = 0;
  cashReceived = 0;
  transferApplied = 0;
  transferReference = '';
  nequiApplied = 0;
  nequiReference = '';
  /** Manual discount over the whole sale; it replaces any per-product discount. */
  generalDiscount: Discount | null = null;
  /** Minimum units of one product for the wholesale price. */
  readonly WHOLESALE_MIN = WHOLESALE_MIN_QUANTITY;
  /**
   * Credentials of the Supervisor/Administrador who authorized a Cajero's discount or wholesale price. They were verified
   * when the concession was applied and are kept only in memory until the sale is confirmed, cancelled or loses its
   * concessions; the server validates them again when the sale is confirmed.
   */
  private concessionAuthorization: AuthorizationCredentials | null = null;
  readonly concessionAuthorizedBy = signal('');
  dueDate = '';
  /** Default term of the credit due date, set by the Administrator; null until read or when the seller must choose. */
  readonly creditDefaultDays = signal<number | null>(null);
  readonly creditRulesKnown = signal(false);
  private pending: { key: string; input: SaleInput; authorizerUserName: string | null } | null = null;
  private suggestTimer: ReturnType<typeof setTimeout> | undefined;
  private suggestSeq = 0;
  @ViewChild('productSearch') private productSearch?: ElementRef<HTMLInputElement>;

  private static readonly AUTO_PRINT_KEY = 'controlplus.sales.autoPrint';
  private static loadAutoPrint(): boolean { try { return localStorage.getItem(Sales.AUTO_PRINT_KEY) !== '0'; } catch { return true; } }
  setAutoPrint(value: boolean): void { this.autoPrint.set(value); try { localStorage.setItem(Sales.AUTO_PRINT_KEY, value ? '1' : '0'); } catch { /* sin almacenamiento: queda solo en esta sesión */ } }

  ngOnInit(): void { if (this.route.snapshot.queryParamMap.get('modalidad') === 'credito' && this.canRequestCredit) this.saleType.set('CREDITO'); void this.refreshCash(); if (this.canRequestCredit) void this.loadCreditRules(); }
  private async loadCreditRules(): Promise<void> {
    try { this.creditDefaultDays.set((await this.api.creditRules()).defaultDays); this.creditRulesKnown.set(true); }
    catch { /* The server still validates the due date; the screen keeps asking for it. */ }
  }
  ngOnDestroy(): void { clearTimeout(this.suggestTimer); }
  get hasCustomerAccess(): boolean { return this.session.has('CUSTOMERS.BASIC_MANAGE') || this.session.has('CUSTOMERS.MANAGE'); }
  get canRequestCredit(): boolean { return this.session.has('SALES.CREATE') && this.session.has('CREDIT.REQUEST'); }
  get operatingDate(): string { return this.cashState()?.currentShift?.operatingDate ?? ''; }
  get minimumDueDate(): string { return this.offsetDate(this.operatingDate, 1); }
  get maximumDueDate(): string { return this.offsetDate(this.operatingDate, 90); }
  /** Due date the default term gives from the cash operating date, never beyond the 90-day limit. */
  get defaultDueDate(): string { const days = this.creditDefaultDays(); return days === null ? '' : this.offsetDate(this.operatingDate, Math.min(days, 90)); }
  get defaultDueDateText(): string { return this.defaultDueDate.split('-').reverse().join('/'); }
  private offsetDate(date: string, days: number): string { if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return ''; const value = new Date(`${date}T00:00:00Z`); value.setUTCDate(value.getUTCDate() + days); return value.toISOString().slice(0, 10); }
  get locked(): boolean { return this.pending !== null; }
  get subtotal(): number { return this.cart().reduce((sum, line) => sum + this.price(line) * line.quantity, 0); }
  get discounts(): number { return this.generalDiscount ? discountAmount(this.subtotal, this.generalDiscount) : this.cart().reduce((sum, line) => sum + this.lineDiscount(line), 0); }
  lineDiscount(line: CartLine): number { return discountAmount(this.price(line) * line.quantity, line.discount); }
  describeDiscount = describeDiscount;
  get maxDiscount(): number { return maxDiscountPercent(this.session.hasRole('ADMINISTRADOR'), this.session.hasRole('SUPERVISOR')); }
  get estimate(): number { return Math.max(0, this.subtotal - this.discounts); }
  get applied(): number { return this.activePayments().reduce((sum, method) => sum + this.paymentAmount(method), 0); }
  get remaining(): number { return this.estimate - this.applied; }
  get change(): number { return Math.max(0, Number(this.cashReceived) - Number(this.cashApplied)); }
  get activePaymentRows(): { code: PaymentCode; name: string }[] {
    return this.activePayments().map(code => ({ code, name: this.paymentName(code) }));
  }
  get canConfirm(): boolean { return !this.busy() && (this.locked || !this.validate()); }
  get selectedCustomerName(): string { return this.customers().find(customer => customer.id === this.customerId)?.fullName ?? 'Consumidor final'; }
  get units(): number { return this.cart().reduce((sum, line) => sum + Number(line.quantity || 0), 0); }

  /** Why the cashier cannot sell yet, with the next step; empty when selling is possible. */
  get blockReason(): string {
    const state = this.cashState();
    if (!state) return '';
    if (state.requiresPreviousShiftClosure) return 'Hay un turno de un día anterior sin cerrar. Ciérralo en Caja antes de vender.';
    if (!state.hasOpenShift) return 'No hay un turno de Caja abierto. Abre el turno en Caja para vender.';
    if (state.requiresOperatorSession && !state.ownOperatorSession) return 'El turno es compartido: inicia tu sesión de operador en Caja para vender.';
    if (!state.requiresOperatorSession && state.currentShift?.responsibleUserId !== this.session.user()?.id) return 'El turno abierto pertenece a otro usuario. Solo su responsable puede vender.';
    return '';
  }

  startCreditSale(): void {
    if (!this.canRequestCredit || this.locked) return;
    this.saleType.set('CREDITO');
    if (!this.cart().length) return;
    this.startCheckout();
  }
  private continueToCheckout = false;
  openCustomerPicker(): void { if (!this.locked) this.showCustomer.set(true); }
  closeCustomerPicker(): void { this.showCustomer.set(false); this.continueToCheckout = false; this.focusSearch(); }
  /** null means «consumidor final». */
  onCustomerChosen(customer: Customer | null): void {
    this.customers.set(customer ? [customer] : []); this.customerId = customer?.id ?? '';
    this.showCustomer.set(false);
    if (this.continueToCheckout && customer) { this.continueToCheckout = false; this.startCheckout(); return; }
    this.continueToCheckout = false; this.focusSearch();
  }
  get selectedCustomer(): Customer | undefined { return this.customers().find(customer => customer.id === this.customerId); }
  startCashSale(): void { if (this.locked) return; this.saleType.set('CONTADO'); this.checkoutMode.set(false); }
  get canSell(): boolean {
    const state = this.cashState();
    return !!state?.hasOpenShift && !state.requiresPreviousShiftClosure && (state.requiresOperatorSession ? !!state.ownOperatorSession : state.currentShift?.responsibleUserId === this.session.user()?.id);
  }
  price(line: CartLine): number { return line.priceType === 'MAYORISTA' ? line.product.wholesalePrice ?? 0 : line.product.retailPrice; }
  paymentName(code: PaymentCode): string { return code === 'EFECTIVO' ? 'Efectivo' : code === 'TRANSFERENCIA' ? 'Transferencia' : 'Nequi'; }
  paymentAmount(code: PaymentCode): number { return code === 'EFECTIVO' ? Number(this.cashApplied) : code === 'TRANSFERENCIA' ? Number(this.transferApplied) : Number(this.nequiApplied); }
  paymentUsed(code: PaymentCode): boolean { return this.activePayments().includes(code); }

  // --- payments -------------------------------------------------------------------------------
  private setAmount(code: PaymentCode, amount: number): void {
    const value = Math.max(0, Math.trunc(Number.isFinite(amount) ? amount : 0));
    if (code === 'EFECTIVO') { this.cashApplied = value; if (Number(this.cashReceived) < value) this.cashReceived = value; }
    if (code === 'TRANSFERENCIA') this.transferApplied = value;
    if (code === 'NEQUI') this.nequiApplied = value;
  }
  /** In a mixed payment the last other method absorbs what is still missing, so the cashier never calculates it. */
  private rebalance(edited: PaymentCode): void {
    const items = this.activePayments();
    if (items.length < 2) return;
    const target = [...items].reverse().find(code => code !== edited);
    if (!target) return;
    const others = items.filter(code => code !== edited && code !== target).reduce((sum, code) => sum + this.paymentAmount(code), 0);
    this.setAmount(target, Math.max(0, this.estimate - this.paymentAmount(edited) - others));
  }
  addPayment(): void {
    if (this.locked) return;
    const next = (['EFECTIVO', 'TRANSFERENCIA', 'NEQUI'] as PaymentCode[]).find(code => !this.paymentUsed(code));
    if (!next) return;
    if (this.paymentUsed('EFECTIVO') && this.cashApplied <= 0 && this.cashReceived > 0) this.cashApplied = Math.min(Number(this.cashReceived), this.estimate);
    const missing = this.estimate - this.applied;
    if (this.applied <= 0) { this.error.set('Escribe cuánto paga el cliente en el primer medio antes de activar el pago mixto.'); this.focusField('cash-received', 'transfer-applied', 'nequi-applied'); return; }
    if (missing <= 0) { this.error.set('El medio actual ya cubre todo el total. Escribe un valor menor si necesitas combinarlo con otro medio.'); this.focusField('cash-received', 'transfer-applied', 'nequi-applied'); return; }
    this.error.set('');
    this.activePayments.update(items => [...items, next]);
    this.setAmount(next, missing);
  }
  setPaymentMethod(current: PaymentCode, next: PaymentCode): void {
    if (current === next || this.locked || this.paymentUsed(next)) return;
    this.activePayments.update(items => items.map(code => code === current ? next : code));
    if (current === 'EFECTIVO') this.cashApplied = this.cashReceived = 0;
    if (current === 'TRANSFERENCIA') { this.transferApplied = 0; this.transferReference = ''; }
    if (current === 'NEQUI') { this.nequiApplied = 0; this.nequiReference = ''; }
    this.setAmount(next, Math.max(0, this.remaining));
  }
  removePayment(code: PaymentCode): void {
    if (this.locked || this.activePayments().length <= 1) return;
    this.activePayments.update(items => items.filter(item => item !== code));
    if (code === 'EFECTIVO') this.cashApplied = this.cashReceived = 0;
    if (code === 'TRANSFERENCIA') { this.transferApplied = 0; this.transferReference = ''; }
    if (code === 'NEQUI') { this.nequiApplied = 0; this.nequiReference = ''; }
    const [only] = this.activePayments();
    if (this.activePayments().length === 1) this.setAmount(only, only === 'EFECTIVO' && this.cashReceived > 0 ? Math.min(Number(this.cashReceived), this.estimate) : this.estimate);
  }
  onCashReceived(value: number): void {
    this.cashReceived = Number.isFinite(value) ? Math.max(0, Math.trunc(value)) : 0;
    this.cashApplied = Math.min(this.cashReceived, this.estimate);
    this.rebalance('EFECTIVO');
  }
  onElectronicAmount(code: 'TRANSFERENCIA' | 'NEQUI', value: number): void {
    this.setAmount(code, Math.min(Number.isFinite(value) ? value : 0, this.estimate));
    this.rebalance(code);
  }
  useRemaining(code: PaymentCode): void {
    this.setAmount(code, Math.max(0, this.remaining + this.paymentAmount(code)));
    if (code === 'EFECTIVO') this.cashReceived = this.cashApplied;
  }
  startCheckout(): void {
    if (!this.cart().length || !this.canSell || this.locked) return;
    this.error.set(''); this.feedback.set('');
    // A credit needs a customer: choose or register one right here before going to the payment screen.
    if (this.saleType() === 'CREDITO' && !this.customerId) {
      if (!this.hasCustomerAccess) { this.error.set('Para vender a crédito se necesita un cliente, y no tienes permiso para consultar ni registrar clientes.'); return; }
      this.continueToCheckout = true; this.openCustomerPicker(); return;
    }
    if (this.saleType() === 'CONTADO') {
      // Por defecto se cobra exacto en efectivo: el cajero solo escribe si el cliente entrega más.
      this.activePayments.set(['EFECTIVO']); this.transferApplied = this.nequiApplied = 0; this.transferReference = this.nequiReference = '';
      this.cashApplied = this.cashReceived = this.estimate;
    }
    // The Administrator's default term is proposed, but the seller can change it; a date already chosen is kept.
    if (this.saleType() === 'CREDITO' && !this.dueDate && this.defaultDueDate) this.dueDate = this.defaultDueDate;
    this.checkoutMode.set(true);
    this.focusField('cash-received', 'credit-due-date');
  }
  returnToSale(): void { if (!this.busy()) { this.checkoutMode.set(false); this.error.set(''); this.focusSearch(); } }
  // --- retail / wholesale price ---------------------------------------------------------------
  /** Applies the choice of a price-type select; when it cannot be applied the select goes back to the current type. */
  async onPriceTypeChange(line: CartLine, select: HTMLSelectElement): Promise<void> {
    const applied = await this.setPriceType(line, select.value as 'MINORISTA' | 'MAYORISTA');
    if (!applied) select.value = line.priceType;
  }
  /**
   * The wholesale price needs at least 12 units of the product and, for a Cajero, the authorization of a Supervisor or
   * Administrador. It never accumulates with a manual discount, so choosing it removes the discounts that no longer apply.
   */
  async setPriceType(line: CartLine, type: 'MINORISTA' | 'MAYORISTA'): Promise<boolean> {
    if (this.locked || line.priceType === type) return false;
    if (type === 'MAYORISTA') {
      if (!line.product.wholesalePrice) { this.error.set(`${line.product.name} no tiene precio mayorista.`); return false; }
      if (Number(line.quantity) < this.WHOLESALE_MIN) { this.error.set(`El precio mayorista exige ${this.WHOLESALE_MIN} o más unidades de ${line.product.name}.`); return false; }
      if (!await this.authorizeConcession('WHOLESALE', `${line.product.name} · ${line.quantity} unidades`)) return false;
    }
    const losesDiscount = type === 'MAYORISTA' && (!!line.discount || !!this.generalDiscount);
    line.priceType = type; line.discount = null;
    if (type === 'MAYORISTA') this.generalDiscount = null;
    if (!this.discounts) this.discountReason = '';
    this.update(); this.error.set(''); this.refreshConcession();
    this.feedbackKind.set(losesDiscount ? 'info' : 'success');
    this.feedback.set(losesDiscount
      ? `${line.product.name}: el precio mayorista no admite descuento, se quitó el descuento.`
      : `${line.product.name}: precio ${type === 'MAYORISTA' ? 'mayorista' : 'minorista'}.`);
    return true;
  }
  // --- authorization of concessions (discount / wholesale price) ------------------------------
  /** A Cajero always needs the authorization of a Supervisor or Administrador for a discount or the wholesale price. */
  get isCashier(): boolean { return !this.session.hasRole('ADMINISTRADOR', 'SUPERVISOR'); }
  get needsConcessionAuthorization(): boolean { return this.isCashier && (this.discounts > 0 || this.hasWholesale); }
  /** Shows the authorization window and keeps the verified credentials for the sale; true when they were accepted. */
  private async authorizeConcession(kind: 'DISCOUNT' | 'WHOLESALE', detail: string): Promise<boolean> {
    if (!this.isCashier) return true;
    return this.authorization.open({
      title: kind === 'DISCOUNT' ? 'Autorizar descuento' : 'Autorizar precio mayorista',
      description: kind === 'DISCOUNT'
        ? 'Un descuento necesita la autorización de un Supervisor o Administrador.'
        : `El precio mayorista solo se aplica desde ${this.WHOLESALE_MIN} unidades y necesita la autorización de un Supervisor o Administrador.`,
      detail, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
      run: async credentials => {
        if (!credentials) return;
        try {
          const result = credentials.credentialToken
            ? await this.api.verifyConcession(kind, '', '', credentials.credentialToken)
            : await this.api.verifyConcession(kind, credentials.userName, credentials.password);
          this.concessionAuthorization = credentials; this.concessionAuthorizedBy.set(result.authorizedBy);
        } catch (error) {
          if (error instanceof HttpErrorResponse && error.status === 403) throw new Error('No se autorizó. Verifica las credenciales o la credencial escaneada, y que sea un Supervisor o Administrador con permiso para autorizar.');
          throw error;
        }
      },
    });
  }
  /** Forgets the credentials once the sale has no discount nor wholesale price left. */
  private refreshConcession(): void {
    if (this.discounts > 0 || this.hasWholesale) return;
    this.concessionAuthorization = null; this.concessionAuthorizedBy.set('');
  }
  // --- discounts (floating window) -------------------------------------------------------------
  /** The line a product discount applies to: the selected one, or the last added. */
  get discountTarget(): CartLine | undefined { return this.cart().find(line => line.product.id === this.selectedProductId()) ?? this.cart().at(-1); }
  get canDiscount(): boolean { return this.session.has('SALES.DISCOUNT_APPLY') && !this.locked && this.cart().length > 0; }
  /** Wholesale prices never accumulate with a manual discount. */
  get hasWholesale(): boolean { return this.cart().some(line => line.priceType === 'MAYORISTA'); }
  get canDiscountProduct(): boolean { return this.canDiscount && this.discountTarget?.priceType !== 'MAYORISTA'; }
  get canDiscountSale(): boolean { return this.canDiscount && !this.hasWholesale; }
  openDiscount(scope: 'product' | 'sale'): void {
    if (scope === 'product' ? !this.canDiscountProduct : !this.canDiscountSale) {
      this.error.set(scope === 'product' ? 'Selecciona un producto de precio minorista para aplicarle un descuento.' : 'La venta tiene productos con precio mayorista: no admite descuento.');
      return;
    }
    this.error.set(''); this.discountScope.set(scope);
  }
  closeDiscount(): void { this.discountScope.set(null); this.focusSearch(); }
  /** What the open window would replace, so the cashier is not surprised. */
  get discountReplaces(): string {
    if (this.discountScope() === 'product' && this.generalDiscount) return 'Se reemplazará el descuento aplicado a la venta total.';
    if (this.discountScope() === 'sale' && this.cart().some(line => line.discount)) return 'Se reemplazarán los descuentos aplicados por producto.';
    return '';
  }
  /** For a Cajero the authorization window opens when the discount is applied; without it the discount is not applied. */
  async applyDiscount(result: DiscountApplied): Promise<void> {
    const scope = this.discountScope(); if (!scope) return;
    const target = scope === 'product' ? this.discountTarget : undefined;
    const detail = scope === 'product' ? `${target?.product.name ?? 'Producto'} · descuento ${describeDiscount(result.discount)}` : `Venta total · descuento ${describeDiscount(result.discount)}`;
    if (!await this.authorizeConcession('DISCOUNT', detail)) return;
    if (scope === 'product') {
      if (!target) return;
      this.generalDiscount = null;
      this.cart.update(lines => lines.map(line => line === target ? { ...line, discount: result.discount } : line));
    } else {
      this.cart.update(lines => lines.map(line => ({ ...line, discount: null })));
      this.generalDiscount = result.discount;
    }
    this.discountReason = result.reason; this.error.set('');
    this.feedbackKind.set('success'); this.feedback.set('Descuento aplicado.');
    this.closeDiscount();
  }
  removeDiscount(scope: 'product' | 'sale' | 'all' = 'all'): void {
    if (this.locked) return;
    const target = this.discountTarget;
    if (scope !== 'product') this.generalDiscount = null;
    if (scope !== 'sale') this.cart.update(lines => lines.map(line => scope === 'all' || line === target ? { ...line, discount: null } : line));
    if (!this.discounts) this.discountReason = '';
    this.refreshConcession();
    this.feedbackKind.set('info'); this.feedback.set('Descuento quitado.');
    if (this.discountScope()) this.closeDiscount();
  }
  validAmount(value: number): boolean { return Number.isSafeInteger(Number(value)) && Number(value) >= 0; }
  async refreshCash(): Promise<void> { try { this.cashState.set(await this.cash.state()); } catch (error) { this.error.set(errorText(error)); } }
  // --- product search with suggestions ---------------------------------------------------------
  /** Called on every keystroke: suggestions appear after two characters, without waiting for Enter. */
  onSearchInput(value: string): void {
    this.search = value; this.activeIndex.set(-1); this.feedback.set('');
    clearTimeout(this.suggestTimer);
    const term = value.trim();
    if (term.length < 2) { this.suggestSeq++; this.results.set([]); return; }
    this.suggestTimer = setTimeout(() => void this.suggest(term), SUGGEST_DELAY_MS);
  }
  private async suggest(term: string): Promise<void> {
    const seq = ++this.suggestSeq;
    try {
      const result = await this.catalog.products({ search: term, page: 1, pageSize: 8 });
      if (seq === this.suggestSeq) this.results.set(result.items);
    } catch (error) { if (seq === this.suggestSeq) this.error.set(errorText(error)); }
  }
  onSearchKey(event: KeyboardEvent): void {
    const total = this.results().length;
    if (event.key === 'ArrowDown' && total) { event.preventDefault(); this.activeIndex.set(Math.min(total - 1, this.activeIndex() + 1)); }
    else if (event.key === 'ArrowUp' && total) { event.preventDefault(); this.activeIndex.set(Math.max(0, this.activeIndex() - 1)); }
    else if (event.key === 'Enter') {
      event.preventDefault();
      const chosen = this.results()[this.activeIndex()];
      if (chosen) this.add(chosen); else void this.find();
    }
  }
  /** «Ver productos»: shows or hides the catalog with an empty search, or the matches of what was typed. */
  async toggleCatalog(): Promise<void> {
    if (this.results().length) { this.results.set([]); this.activeIndex.set(-1); this.focusSearch(); return; }
    const term = this.search.trim();
    if (term) { await this.find(true); return; }
    clearTimeout(this.suggestTimer); this.suggestSeq++;
    this.loading.set(true); this.error.set('');
    try {
      const result = await this.catalog.products({ page: 1, pageSize: 30 });
      this.results.set(result.items);
      this.feedbackKind.set(result.items.length ? 'info' : 'error');
      this.feedback.set(result.items.length ? 'Elige un producto de la lista.' : 'No hay productos disponibles para vender.');
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }
  /** Enter / «Agregar»: an exact code or barcode is added at once; otherwise the matches are listed. */
  async find(listOnly = false): Promise<void> {
    const term = this.search.trim();
    if (!term) { this.feedbackKind.set('error'); this.feedback.set('Escribe o escanea un producto.'); return; }
    clearTimeout(this.suggestTimer); this.suggestSeq++;
    this.loading.set(true); this.error.set('');
    try {
      const result = await this.catalog.products({ search: term, page: 1, pageSize: 30 });
      const exact = result.items.filter(item => item.internalCode === term || item.barcode === term);
      const single = exact.length === 1 ? exact[0] : result.items.length === 1 ? result.items[0] : null;
      if (single && !this.locked && !listOnly) { this.add(single); this.results.set([]); }
      else {
        this.results.set(result.items);
        this.feedbackKind.set(result.items.length ? 'info' : 'error');
        this.feedback.set(result.items.length ? 'Hay varias coincidencias. Elige el producto de la lista.' : `No encontramos «${term}». Revisa el código o el nombre.`);
      }
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }
  add(product: Product): void {
    if (this.locked) return;
    if (!product.isActive || product.availableStock < 1) { this.error.set(`${product.name ?? 'El producto'} está agotado o inactivo.`); return; }
    const lines = this.cart(); const existing = lines.find(line => line.product.id === product.id && line.priceType === 'MINORISTA' && !line.discount);
    if (existing && existing.quantity + 1 > product.availableStock) { this.error.set(`Solo hay ${product.availableStock} unidades disponibles de ${product.name}.`); return; }
    if (existing) this.cart.set(lines.map(line => line === existing ? { ...line, quantity: line.quantity + 1 } : line));
    else this.cart.set([...lines, { product, quantity: 1, priceType: 'MINORISTA', discount: null }]);
    this.selectedProductId.set(product.id);
    this.results.set([]); this.activeIndex.set(-1); this.search = ''; this.error.set('');
    const quantity = this.cart().find(line => line.product.id === product.id)?.quantity ?? 1;
    this.feedbackKind.set('success');
    this.feedback.set(`${product.name} agregado. Cantidad actual: ${quantity}.`);
    this.focusSearch();
    this.scrollToLine(product.id);
  }
  remove(id: string): void {
    if (this.locked) return;
    const next = this.cart().filter(line => line.product.id !== id);
    this.cart.set(next);
    if (this.selectedProductId() === id) this.selectedProductId.set(next[0]?.product.id ?? null);
    if (!this.discounts) this.discountReason = '';
    this.refreshConcession();
  }
  removeLast(): void { const id = this.cart().at(-1)?.product.id; if (id) this.remove(id); }
  removeSelected(): void { const id = this.selectedProductId() ?? this.cart().at(-1)?.product.id; if (id) this.remove(id); }
  update(): void { this.cart.set([...this.cart()]); }
  /** Keeps the quantity an integer between 1 and the available stock once the cashier leaves the field. */
  normalizeQuantity(line: CartLine): void {
    const quantity = Math.trunc(Number(line.quantity));
    const max = line.product.availableStock;
    if (!Number.isFinite(quantity) || quantity < 1) line.quantity = 1;
    else if (quantity > max) { line.quantity = max; this.error.set(`Solo hay ${max} unidades disponibles de ${line.product.name}.`); }
    else line.quantity = quantity;
    if (line.priceType === 'MAYORISTA' && Number(line.quantity) < this.WHOLESALE_MIN) {
      line.priceType = 'MINORISTA';
      this.feedbackKind.set('info');
      this.feedback.set(`${line.product.name}: volvió a precio minorista, el mayorista exige ${this.WHOLESALE_MIN} o más unidades.`);
    }
    this.update(); this.refreshConcession();
  }
  focusQuantity(): void {
    const id = this.selectedProductId() ?? this.cart()[0]?.product.id;
    if (!id) { this.error.set('Selecciona primero un producto.'); return; }
    this.selectedProductId.set(id);
    const input = typeof document === 'undefined' ? null : document.getElementById(`quantity-${id}`) as HTMLInputElement | null;
    input?.focus(); input?.select();
  }
  focusSearch(): void { this.productSearch?.nativeElement.focus(); }
  private focusField(...ids: string[]): void {
    if (typeof document === 'undefined') return;
    setTimeout(() => { const input = ids.map(id => document.getElementById(id) as HTMLInputElement | null).find(Boolean); input?.focus(); input?.select?.(); });
  }
  private scrollToLine(id: string): void {
    if (typeof document === 'undefined') return;
    setTimeout(() => document.getElementById(`line-${id}`)?.scrollIntoView?.({ block: 'nearest' }));
  }
  cancelSale(): void {
    if (this.locked) return;
    this.cart.set([]); this.results.set([]); this.selectedProductId.set(null); this.generalDiscount = null; this.discountReason = '';
    this.concessionAuthorization = null; this.concessionAuthorizedBy.set('');
    this.showCancel.set(false); this.feedbackKind.set('info'); this.feedback.set('Venta cancelada. No se registró ningún cobro.'); this.focusSearch();
  }

  // --- keyboard -------------------------------------------------------------------------------
  @HostListener('document:keydown', ['$event'])
  handleShortcut(event: KeyboardEvent): void {
    if (typeof document !== 'undefined' && document.querySelector('.cdk-overlay-container .mat-mdc-dialog-container')) return;
    if (this.showCustomer()) return;
    if (this.sale()) {
      const onControl = event.target instanceof HTMLElement && !!event.target.closest('button, input, select, textarea, a');
      if (event.key === 'Enter' && !onControl) { event.preventDefault(); this.newSale(); }
      return;
    }
    if (this.showCancel()) { if (event.key === 'Escape') { event.preventDefault(); this.showCancel.set(false); } return; }
    if (this.checkoutMode()) {
      if (event.key === 'Escape') { event.preventDefault(); this.returnToSale(); }
      if (event.key === 'F9') { event.preventDefault(); void this.confirm(); }
      return;
    }
    const target = event.target instanceof HTMLElement ? event.target : null;
    const editing = !!target?.closest('input, textarea, select, [contenteditable=true]');
    if (event.key === 'F2') { event.preventDefault(); this.focusSearch(); }
    if (event.key === 'F3') { event.preventDefault(); if (this.hasCustomerAccess) this.openCustomerPicker(); }
    if (event.key === 'F4') { event.preventDefault(); this.focusQuantity(); }
    if (event.key === 'F9') { event.preventDefault(); this.startCheckout(); }
    if (event.key === 'Delete' && !editing) { event.preventDefault(); this.removeSelected(); }
    if (event.key === 'Escape' && this.results().length) { event.preventDefault(); this.results.set([]); this.activeIndex.set(-1); this.focusSearch(); }
  }
  @HostListener('document:click', ['$event'])
  dismissResults(event: Event): void {
    if (event.target instanceof Element && !event.target.closest('.search-area')) { this.results.set([]); this.activeIndex.set(-1); }
  }

  // --- confirmation ---------------------------------------------------------------------------
  private validate(): string | null {
    if (!this.canSell) return this.blockReason || 'Se necesita un turno válido y, en modalidad compartida, una sesión de operador activa.';
    if (!this.cart().length) return 'Agrega al menos un producto.';
    if (this.cart().some(line => !Number.isSafeInteger(Number(line.quantity)) || line.quantity < 1 || line.quantity > line.product.availableStock || (line.priceType === 'MAYORISTA' && (!line.product.wholesalePrice || !!line.discount)))) return 'Revisa cantidades, existencias y precio mayorista.';
    const short = this.cart().find(line => line.priceType === 'MAYORISTA' && Number(line.quantity) < this.WHOLESALE_MIN);
    if (short) return `El precio mayorista exige ${this.WHOLESALE_MIN} o más unidades de ${short.product.name}.`;
    const max = this.maxDiscount;
    if (this.cart().some(line => line.discount) || this.generalDiscount) {
      if (!this.session.has('SALES.DISCOUNT_APPLY')) return 'No tienes permiso para aplicar descuentos.';
      if (this.generalDiscount && this.hasWholesale) return 'La venta tiene productos con precio mayorista: no admite descuento.';
      const general = this.generalDiscount ? validateDiscount(this.generalDiscount, this.subtotal, max) : '';
      if (general) return general;
      for (const line of this.cart()) {
        const issue = line.discount ? validateDiscount(line.discount, this.price(line) * line.quantity, max) : '';
        if (issue) return `${line.product.name}: ${issue}`;
      }
    }
    if (this.saleType() === 'CONTADO' && [this.cashApplied, this.cashReceived, this.transferApplied, this.nequiApplied].some(value => !this.validAmount(value))) return 'Los pagos deben ser enteros no negativos.';
    if (this.estimate <= 0) return 'El total de la venta debe ser mayor que cero.';
    if (this.saleType() === 'CREDITO') {
      if (!this.canRequestCredit) return 'No tienes permiso para solicitar créditos.';
      const customer = this.customers().find(item => item.id === this.customerId);
      if (!customer?.isActive) return 'Selecciona un cliente activo para el crédito.';
      if (!(customer.documentType && customer.documentNumber) && !customer.phone) return 'El cliente necesita documento completo o teléfono para crédito.';
      if (!this.operatingDate || !this.dueDate || this.dueDate < this.minimumDueDate || this.dueDate > this.maximumDueDate) return 'El vencimiento debe estar entre mañana y 90 días desde la fecha de caja.';
    } else {
      if (this.applied !== this.estimate) return this.activePayments().length === 1 && this.paymentUsed('EFECTIVO') && this.applied < this.estimate
        ? 'El efectivo no cubre toda la venta. Pulsa «+ Pago mixto» para agregar otro medio.' : 'Los importes de pago deben cubrir el total exacto.';
      if (this.paymentUsed('EFECTIVO') && this.cashApplied > 0 && this.cashReceived < this.cashApplied) return 'El efectivo recibido debe cubrir el valor aplicado.';
      if (this.paymentUsed('TRANSFERENCIA') && this.transferApplied > 0 && !this.transferReference.trim()) return 'Ingresa la referencia de la transferencia.';
      if (this.paymentUsed('NEQUI') && this.nequiApplied > 0 && !this.nequiReference.trim()) return 'Ingresa la referencia de Nequi.';
    }
    if (this.customerId.trim() && !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(this.customerId.trim())) return 'El identificador del cliente no es válido.';
    return null;
  }
  /** Enter inside the payment fields confirms the sale, like the prototype; buttons and lists keep their own Enter. */
  checkoutEnter(event: Event): void {
    const target = event.target instanceof HTMLElement ? event.target : null;
    if (target?.closest('button, select, textarea, a')) return;
    event.preventDefault(); void this.confirm();
  }
  /** Moves the cursor to the field that blocks the confirmation. */
  private focusInvalid(): void {
    if (this.saleType() === 'CREDITO') { this.focusField('credit-due-date'); return; }
    if (this.paymentUsed('TRANSFERENCIA') && this.transferApplied > 0 && !this.transferReference.trim()) { this.focusField('transfer-reference'); return; }
    if (this.paymentUsed('NEQUI') && this.nequiApplied > 0 && !this.nequiReference.trim()) { this.focusField('nequi-reference'); return; }
    this.focusField('cash-received', 'transfer-applied', 'nequi-applied');
  }
  async confirm(): Promise<void> {
    if (this.busy() || !this.checkoutMode()) return;
    const issue = this.pending ? null : this.validate();
    if (issue) { this.error.set(issue); this.focusInvalid(); return; }
    if (this.saleType() === 'CREDITO') {
      await this.authorization.open({ title: 'Autorizar venta a crédito',
        description: 'Se registrará la venta a crédito, se descontará inventario y se abrirá la deuda del cliente.' + (this.needsConcessionAuthorization ? ' También autoriza el descuento o el precio mayorista.' : ''),
        detail: `${this.selectedCustomerName} · ${this.estimate} COP · vence ${this.dueDate}`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR', 'SUPERVISOR'],
        onError: message => this.error.set(message),
        run: credentials => this.submitSale(credentials) });
    } else if (this.needsConcessionAuthorization) {
      // Authorized when the discount or wholesale price was applied; if the credentials are gone they are asked again.
      if (this.concessionAuthorization) {
        try { await this.submitSale(this.concessionAuthorization); }
        catch (error) { this.error.set(error instanceof Error ? error.message : errorText(error)); }
      } else await this.authorization.open({ title: 'Autorizar descuento o precio mayorista',
        description: 'Un descuento o el precio mayorista necesitan la autorización de un Supervisor o Administrador.',
        detail: `${this.selectedCustomerName} · descuento ${this.discounts} COP · total ${this.estimate} COP`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
        onError: message => this.error.set(message),
        run: credentials => this.submitSale(credentials) });
    } else await this.submitSale(null);
  }
  private async submitSale(credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.busy()) return;
    if (this.pending?.authorizerUserName && this.pending.authorizerUserName !== authorizationIdentity(credentials))
      throw new Error('Reintenta con el mismo usuario autorizador; la clave de esta venta conserva su identidad.');
    if (!this.pending) {
      const payments: SalePaymentInput[] = this.saleType() === 'CREDITO' ? [] : this.activePayments().flatMap(code => {
        const amount = this.paymentAmount(code);
        if (!amount) return [];
        return [{ methodCode: code, appliedAmount: amount,
          receivedAmount: code === 'EFECTIVO' ? Number(this.cashReceived) : null,
          reference: code === 'TRANSFERENCIA' ? this.transferReference.trim() || null : code === 'NEQUI' ? this.nequiReference.trim() || null : null }];
      });
      this.pending = { key: crypto.randomUUID(), authorizerUserName: authorizationIdentity(credentials), input: { customerId: this.customerId.trim() || null, lines: this.cart().map(line => ({ productId: line.product.id, quantity: Number(line.quantity), priceType: line.priceType, discount: !this.generalDiscount && line.discount ? { type: line.discount.type, value: Number(line.discount.value) } : null })), generalDiscount: this.generalDiscount ? { type: this.generalDiscount.type, value: Number(this.generalDiscount.value) } : null, discountReason: this.discountReason.trim() || null, payments, saleType: this.saleType(), dueDate: this.saleType() === 'CREDITO' ? this.dueDate : null, authorization: null } };
    }
    this.busy.set(true); this.error.set('');
    try {
      const meta: ReceiptMeta = { customer: this.selectedCustomerName, operator: this.session.user()?.displayName ?? '', items: this.cart().length, units: this.units };
      const result = await this.api.confirm({ ...this.pending.input, authorization: credentials ? authorizationBody(credentials) : null }, this.pending.key);
      this.receipt.set(meta); this.showDetail.set(false); this.reprintMessage.set(''); this.printed.set(false); this.lastJobStatus.set(null); this.sale.set(result);
      this.pending = null; this.cart.set([]); this.results.set([]); this.selectedProductId.set(null); this.cashApplied = this.cashReceived = this.transferApplied = this.nequiApplied = 0; this.generalDiscount = null; this.concessionAuthorization = null; this.concessionAuthorizedBy.set(''); this.activePayments.set(['EFECTIVO']); this.checkoutMode.set(false);
      this.focusField('new-sale');
      // La venta ya está confirmada: un fallo de impresión no la afecta y se puede reintentar con el botón.
      setTimeout(() => void this.printSaleTicket(true));
      await this.refreshCash();
    } catch (error) { if (error instanceof HttpErrorResponse && error.status === 403 && this.pending?.input.saleType === 'CREDITO') { this.pending = null; throw new Error('No se autorizó el crédito. Verifica el rol, permiso y credenciales del autorizador.'); } if (error instanceof HttpErrorResponse && error.status === 403 && credentials) { this.pending = null; this.concessionAuthorization = null; this.concessionAuthorizedBy.set(''); throw new Error('No se autorizó el descuento o el precio mayorista. Aplica la autorización de nuevo con un Supervisor o Administrador.'); } if (credentials) throw error; this.error.set(`${errorText(error)} Si la respuesta se perdió, reintenta la misma venta con la misma clave.`); }
    finally { this.busy.set(false); }
  }

  // --- after the sale -------------------------------------------------------------------------
  /** Clears the finished sale and leaves the screen ready for the next customer. */
  newSale(): void {
    this.sale.set(null); this.receipt.set(null); this.showDetail.set(false); this.reprintMessage.set(''); this.printed.set(false); this.lastJobStatus.set(null);
    this.customerId = ''; this.customers.set([]); this.discountReason = ''; this.dueDate = '';
    this.concessionAuthorization = null; this.concessionAuthorizedBy.set('');
    this.saleType.set('CONTADO'); this.checkoutMode.set(false); this.feedback.set(''); this.error.set('');
    this.focusField('product-search');
  }
  async consult(): Promise<void> {
    const id = this.sale()?.id; if (!id) return;
    this.busy.set(true); this.error.set('');
    try { this.sale.set(await this.api.get(id)); } catch (error) { this.error.set(errorText(error)); }
    finally { this.busy.set(false); }
  }
  /** First print goes straight to the printer; every later print records a traceable reprint request first. */
  async printSaleTicket(automatic = false): Promise<void> {
    const sale = this.sale(); if (!sale || this.busy()) return;
    const reprint = this.printed();
    this.busy.set(true); this.reprintMessage.set('');
    try {
      const status = await this.api.printingStatus();
      if (status.hasActiveProfile) {
        let jobs = (await this.api.printJobs(sale.id)).items;
        let latest = jobs.at(-1);
        if (!automatic && reprint && latest?.status === 'PENDIENTE') {
          this.reprintMessage.set('El ticket sigue pendiente. Consulta el estado antes de solicitar otra copia.');
          return;
        }
        if (!automatic && reprint && latest?.status === 'IMPRIMIENDO') {
          this.reprintMessage.set('Resultado incierto: revisa la cola de Windows antes de solicitar otra copia.');
          return;
        }
        if (!automatic && reprint && latest?.status === 'ERROR') {
          await this.api.retryPrintJob(sale.id, latest.id);
          jobs = (await this.api.printJobs(sale.id)).items;
          latest = jobs.at(-1);
        } else if (!automatic && reprint) {
          await this.api.requestReprint(sale.id, crypto.randomUUID());
          jobs = (await this.api.printJobs(sale.id)).items;
          latest = jobs.at(-1);
        }
        this.printed.set(latest != null);
        this.lastJobStatus.set(latest?.status ?? null);
        this.reprintMessage.set(latest
          ? `${latest.copyType === 'REIMPRESION' ? 'Reimpresión' : 'Ticket'}: ${latest.status}. ${latest.status === 'IMPRESO' ? 'Aceptado por la cola de Windows.' : ''}`
          : 'La venta está confirmada; consulta el estado de impresión antes de solicitar otra copia.');
        return;
      }
      const existing = (await this.api.printJobs(sale.id)).items.at(-1);
      if (existing) {
        this.printed.set(true);
        this.lastJobStatus.set(existing.status);
        this.reprintMessage.set(`Ticket: ${existing.status}. Revisa la cola de Windows antes de solicitar otra copia.`);
        return;
      }
      if (automatic && !this.autoPrint()) return;
      this.lastJobStatus.set(null);
      if (reprint) {
        await this.api.requestReprint(sale.id, crypto.randomUUID());
        const queued = (await this.api.printJobs(sale.id)).items.at(-1);
        if (queued?.copyType === 'REIMPRESION') {
          this.lastJobStatus.set(queued.status);
          this.reprintMessage.set(`Reimpresión: ${queued.status}. Consulta la cola de Windows.`);
          return;
        }
      }
      const receipt = await this.api.receipt(sale.id);
      const meta = this.receipt();
      await this.printer.print(buildTicketHtml(receipt, { operator: receipt.operatorName ?? meta?.operator, customer: receipt.customerName ?? meta?.customer }, reprint));
      this.printed.set(true);
      this.reprintMessage.set(reprint ? 'Reimpresión enviada y registrada.' : 'Ticket enviado a la impresora.');
    } catch (error) { this.reprintMessage.set(errorText(error)); }
    finally { this.busy.set(false); }
  }
  formatDate(iso: string | undefined): string {
    const date = iso ? new Date(iso) : new Date();
    return new Intl.DateTimeFormat('es-CO', { dateStyle: 'long', timeStyle: 'short' }).format(date);
  }
  discardPending(): void { this.pending = null; this.error.set(''); }
}
