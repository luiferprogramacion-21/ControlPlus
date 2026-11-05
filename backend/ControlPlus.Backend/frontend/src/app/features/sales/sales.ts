import { Component, ElementRef, HostListener, inject, OnInit, signal, ViewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { CashApi, CashState } from '../cash/cash-api';
import { Sale, SaleInput, SalesApi, SalePaymentInput } from './sales-api';
import { Customer } from '../customers/customers-api';
import { AuthorizationCredentials, AuthorizationDialog } from '../../shared/authorization-dialog';

interface CartLine { product: Product; quantity: number; priceType: 'MINORISTA' | 'MAYORISTA'; discountPercent: number; }
type PaymentCode = 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI';
@Component({ selector: 'cp-sales', imports: [FormsModule, CurrencyPipe, DatePipe, MatButtonModule], templateUrl: './sales.html', styleUrl: './sales.scss' })
export class Sales implements OnInit {
  private readonly catalog = inject(CatalogApi);
  private readonly cash = inject(CashApi);
  private readonly api = inject(SalesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly authorization = inject(AuthorizationDialog);
  readonly session = inject(SessionService);
  readonly cashState = signal<CashState | null>(null);
  readonly results = signal<Product[]>([]);
  readonly cart = signal<CartLine[]>([]);
  readonly sale = signal<Sale | null>(null);
  readonly checkoutMode = signal(false);
  readonly saleType = signal<'CONTADO' | 'CREDITO'>('CONTADO');
  readonly activePayments = signal<PaymentCode[]>(['EFECTIVO']);
  readonly lineDiscountMode = signal(false);
  readonly selectedProductId = signal<string | null>(null);
  readonly customers = signal<Customer[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  search = '';
  customerId = '';
  customerSearch = '';
  discountReason = '';
  cashApplied = 0;
  cashReceived = 0;
  transferApplied = 0;
  transferReference = '';
  nequiApplied = 0;
  nequiReference = '';
  generalDiscountPercent = 0;
  dueDate = '';
  private pending: { key: string; input: SaleInput; authorizerUserName: string | null } | null = null;
  @ViewChild('productSearch') private productSearch?: ElementRef<HTMLInputElement>;
  ngOnInit(): void { if (this.route.snapshot.queryParamMap.get('modalidad') === 'credito' && this.canRequestCredit) this.saleType.set('CREDITO'); void this.refreshCash(); }
  get canRequestCredit(): boolean { return this.session.has('SALES.CREATE') && this.session.has('CREDIT.REQUEST'); }
  get operatingDate(): string { return this.cashState()?.currentShift?.operatingDate ?? ''; }
  get minimumDueDate(): string { return this.offsetDate(this.operatingDate, 1); }
  get maximumDueDate(): string { return this.offsetDate(this.operatingDate, 90); }
  private offsetDate(date: string, days: number): string { if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return ''; const value = new Date(`${date}T00:00:00Z`); value.setUTCDate(value.getUTCDate() + days); return value.toISOString().slice(0, 10); }
  get locked(): boolean { return this.pending !== null; }
  get subtotal(): number { return this.cart().reduce((sum, line) => sum + this.price(line) * line.quantity, 0); }
  get discounts(): number { return this.generalDiscountPercent ? Math.round(this.subtotal * this.generalDiscountPercent / 100) : this.cart().reduce((sum, line) => sum + Math.round(this.price(line) * line.quantity * line.discountPercent / 100), 0); }
  get estimate(): number { return Math.max(0, this.subtotal - this.discounts); }
  get applied(): number { return this.activePayments().reduce((sum, method) => sum + this.paymentAmount(method), 0); }
  get remaining(): number { return this.estimate - this.applied; }
  get change(): number { return Math.max(0, Number(this.cashReceived) - Number(this.cashApplied)); }
  get activePaymentRows(): { code: PaymentCode; name: string }[] {
    return this.activePayments().map(code => ({ code, name: this.paymentName(code) }));
  }
  get canConfirm(): boolean { return !this.busy() && (this.locked || !this.validate()); }
  get selectedCustomerName(): string { return this.customers().find(customer => customer.id === this.customerId)?.fullName ?? 'Consumidor final'; }
  startCreditSale(): void { if (!this.canRequestCredit || this.locked) return; this.saleType.set('CREDITO'); if (this.cart().length) this.startCheckout(); }
  startCashSale(): void { if (this.locked) return; this.saleType.set('CONTADO'); this.checkoutMode.set(false); }
  get canSell(): boolean {
    const state = this.cashState();
    return !!state?.hasOpenShift && !state.requiresPreviousShiftClosure && (state.requiresOperatorSession ? !!state.ownOperatorSession : state.currentShift?.responsibleUserId === this.session.user()?.id);
  }
  price(line: CartLine): number { return line.priceType === 'MAYORISTA' ? line.product.wholesalePrice ?? 0 : line.product.retailPrice; }
  paymentName(code: PaymentCode): string { return code === 'EFECTIVO' ? 'Efectivo' : code === 'TRANSFERENCIA' ? 'Transferencia' : 'Nequi'; }
  paymentAmount(code: PaymentCode): number { return code === 'EFECTIVO' ? Number(this.cashApplied) : code === 'TRANSFERENCIA' ? Number(this.transferApplied) : Number(this.nequiApplied); }
  paymentUsed(code: PaymentCode): boolean { return this.activePayments().includes(code); }
  addPayment(): void {
    if (this.locked) return;
    const next = (['EFECTIVO', 'TRANSFERENCIA', 'NEQUI'] as PaymentCode[]).find(code => !this.paymentUsed(code));
    if (!next) return;
    this.activePayments.update(items => [...items, next]);
    if (next === 'EFECTIVO') { this.cashApplied = Math.max(0, this.remaining); this.cashReceived = this.cashApplied; }
    if (next === 'TRANSFERENCIA') this.transferApplied = Math.max(0, this.remaining);
    if (next === 'NEQUI') this.nequiApplied = Math.max(0, this.remaining);
  }
  setPaymentMethod(current: PaymentCode, next: PaymentCode): void {
    if (current === next || this.locked || this.paymentUsed(next)) return;
    this.activePayments.update(items => items.map(code => code === current ? next : code));
    if (current === 'EFECTIVO') this.cashApplied = this.cashReceived = 0;
    if (current === 'TRANSFERENCIA') { this.transferApplied = 0; this.transferReference = ''; }
    if (current === 'NEQUI') { this.nequiApplied = 0; this.nequiReference = ''; }
    const balance = Math.max(0, this.remaining);
    if (next === 'EFECTIVO') { this.cashApplied = balance; this.cashReceived = balance; }
    if (next === 'TRANSFERENCIA') this.transferApplied = balance;
    if (next === 'NEQUI') this.nequiApplied = balance;
  }
  removePayment(code: PaymentCode): void {
    if (this.locked || this.activePayments().length <= 1) return;
    this.activePayments.update(items => items.filter(item => item !== code));
    if (code === 'EFECTIVO') this.cashApplied = this.cashReceived = 0;
    if (code === 'TRANSFERENCIA') { this.transferApplied = 0; this.transferReference = ''; }
    if (code === 'NEQUI') { this.nequiApplied = 0; this.nequiReference = ''; }
  }
  onCashReceived(value: number): void {
    this.cashReceived = Number.isFinite(value) ? Math.max(0, Math.trunc(value)) : 0;
    const other = this.activePayments().filter(code => code !== 'EFECTIVO').reduce((sum, code) => sum + this.paymentAmount(code), 0);
    this.cashApplied = Math.max(0, Math.min(this.cashReceived, this.estimate - other));
  }
  useRemaining(code: PaymentCode): void {
    const amount = Math.max(0, this.remaining + this.paymentAmount(code));
    if (code === 'EFECTIVO') { this.cashApplied = amount; this.cashReceived = amount; }
    if (code === 'TRANSFERENCIA') this.transferApplied = amount;
    if (code === 'NEQUI') this.nequiApplied = amount;
  }
  startCheckout(): void { if (this.cart().length && this.canSell && !this.locked) { this.error.set(''); this.checkoutMode.set(true); } }
  returnToSale(): void { if (!this.busy()) { this.checkoutMode.set(false); this.error.set(''); } }
  applyGeneralDiscount(value: number): void {
    this.generalDiscountPercent = Number.isFinite(value) ? Math.max(0, Math.trunc(value)) : 0;
    if (this.generalDiscountPercent > 0) this.cart.update(lines => lines.map(line => ({ ...line, discountPercent: 0 })));
  }
  validAmount(value: number): boolean { return Number.isSafeInteger(Number(value)) && Number(value) >= 0; }
  async refreshCash(): Promise<void> { try { this.cashState.set(await this.cash.state()); } catch (error) { this.error.set(errorText(error)); } }
  async findCustomers(): Promise<void> {
    if (!this.session.has('CUSTOMERS.BASIC_MANAGE') && !this.session.has('CUSTOMERS.MANAGE')) { this.error.set('No tienes permiso para consultar clientes. La venta de contado puede continuar sin cliente.'); return; }
    if (!this.customerSearch.trim()) { this.customers.set([]); this.customerId = ''; return; }
    this.loading.set(true); this.error.set('');
    try { const items = (await this.api.customers(this.customerSearch.trim())).items.filter(x => x.isActive); this.customers.set(items); if (!items.some(x => x.id === this.customerId)) this.customerId = ''; }
    catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }
  async find(): Promise<void> {
    if (!this.search.trim()) return;
    this.loading.set(true); this.error.set('');
    try {
      const result = await this.catalog.products({ search: this.search.trim(), page: 1, pageSize: 30 });
      const exact = result.items.filter(item => item.internalCode === this.search.trim() || item.barcode === this.search.trim());
      if (exact.length === 1 && !this.locked) { this.add(exact[0]); this.results.set([]); this.search = ''; }
      else this.results.set(result.items);
    } catch (error) { this.error.set(errorText(error)); }
    finally { this.loading.set(false); }
  }
  add(product: Product): void {
    if (this.locked) return;
    if (!product.isActive || product.availableStock < 1) { this.error.set('Producto inactivo o agotado.'); return; }
    const lines = this.cart(); const existing = lines.find(line => line.product.id === product.id && line.priceType === 'MINORISTA' && line.discountPercent === 0);
    if (existing) this.cart.set(lines.map(line => line === existing ? { ...line, quantity: line.quantity + 1 } : line));
    else this.cart.set([...lines, { product, quantity: 1, priceType: 'MINORISTA', discountPercent: 0 }]);
    this.selectedProductId.set(product.id);
    this.results.set([]); this.search = '';
  }
  remove(id: string): void {
    if (this.locked) return;
    const next = this.cart().filter(line => line.product.id !== id);
    this.cart.set(next);
    if (this.selectedProductId() === id) this.selectedProductId.set(next[0]?.product.id ?? null);
  }
  removeLast(): void { const id = this.cart().at(-1)?.product.id; if (id) this.remove(id); }
  removeSelected(): void { const id = this.selectedProductId() ?? this.cart().at(-1)?.product.id; if (id) this.remove(id); }
  update(): void { this.cart.set([...this.cart()]); }
  focusQuantity(): void {
    const id = this.selectedProductId() ?? this.cart()[0]?.product.id;
    if (id) document.getElementById(`quantity-${id}`)?.focus();
  }
  @HostListener('document:keydown', ['$event'])
  handleShortcut(event: KeyboardEvent): void {
    if (this.checkoutMode()) { if (event.key === 'Escape') this.returnToSale(); return; }
    const target = event.target instanceof HTMLElement ? event.target : null;
    const editing = !!target?.closest('input, textarea, select, [contenteditable=true]');
    if (event.key === 'F2') { event.preventDefault(); this.productSearch?.nativeElement.focus(); }
    if (event.key === 'F4') { event.preventDefault(); this.focusQuantity(); }
    if (event.key === 'F9') { event.preventDefault(); this.startCheckout(); }
    if (event.key === 'Delete' && !editing) { event.preventDefault(); this.removeSelected(); }
    if (event.key === 'Escape' && this.results().length) { event.preventDefault(); this.results.set([]); this.productSearch?.nativeElement.focus(); }
  }
  private validate(): string | null {
    if (!this.canSell) return 'Se necesita un turno válido y, en modalidad compartida, una sesión de operador activa.';
    if (!this.cart().length) return 'Agrega al menos un producto.';
    if (this.cart().some(line => !Number.isSafeInteger(Number(line.quantity)) || line.quantity < 1 || line.quantity > line.product.availableStock || (line.priceType === 'MAYORISTA' && (!line.product.wholesalePrice || line.discountPercent !== 0)))) return 'Revisa cantidades, existencias y precio mayorista.';
    const max = this.session.hasRole('ADMINISTRADOR') ? 80 : this.session.hasRole('SUPERVISOR') ? 20 : 5;
    if (this.cart().some(line => !this.validAmount(line.discountPercent) || line.discountPercent > max || (line.discountPercent > 0 && !this.session.has('SALES.DISCOUNT_APPLY')))) return `Revisa el descuento permitido (máximo ${max} %).`;
    if (this.cart().some(line => line.discountPercent > 0) && !this.discountReason.trim()) return 'Indica el motivo del descuento.';
    if (this.saleType() === 'CONTADO' && [this.cashApplied, this.cashReceived, this.transferApplied, this.nequiApplied].some(value => !this.validAmount(value))) return 'Los pagos deben ser enteros no negativos.';
    if (!this.validAmount(this.generalDiscountPercent) || this.generalDiscountPercent > max || (this.generalDiscountPercent > 0 && !this.session.has('SALES.DISCOUNT_APPLY'))) return `Revisa el descuento permitido (máximo ${max} %).`;
    if (this.estimate <= 0) return 'El total de la venta debe ser mayor que cero.';
    if (this.saleType() === 'CREDITO') {
      if (!this.canRequestCredit) return 'No tienes permiso para solicitar créditos.';
      const customer = this.customers().find(item => item.id === this.customerId);
      if (!customer?.isActive) return 'Selecciona un cliente activo para el crédito.';
      if (!(customer.documentType && customer.documentNumber) && !customer.phone) return 'El cliente necesita documento completo o teléfono para crédito.';
      if (!this.operatingDate || !this.dueDate || this.dueDate < this.minimumDueDate || this.dueDate > this.maximumDueDate) return 'El vencimiento debe estar entre mañana y 90 días desde la fecha de caja.';
    } else {
      if (this.applied !== this.estimate) return 'Los importes de pago deben cubrir el total exacto.';
      if (this.paymentUsed('EFECTIVO') && this.cashApplied > 0 && this.cashReceived < this.cashApplied) return 'El efectivo recibido debe cubrir el valor aplicado.';
      if (this.paymentUsed('TRANSFERENCIA') && this.transferApplied > 0 && !this.transferReference.trim()) return 'Ingresa la referencia de la transferencia.';
      if (this.paymentUsed('NEQUI') && this.nequiApplied > 0 && !this.nequiReference.trim()) return 'Ingresa la referencia de Nequi.';
    }
    if (this.customerId.trim() && !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(this.customerId.trim())) return 'El identificador del cliente no es válido.';
    return null;
  }
  async confirm(): Promise<void> {
    if (this.busy()) return;
    const issue = this.pending ? null : this.validate();
    if (issue) { this.error.set(issue); return; }
    if (this.saleType() === 'CREDITO') {
      await this.authorization.open({ title: 'Autorizar venta a crédito',
        description: 'Se registrará la venta a crédito, se descontará inventario y se abrirá la deuda del cliente.',
        detail: `${this.selectedCustomerName} · ${this.estimate} COP · vence ${this.dueDate}`,
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, selfAuthorizedRoles: ['ADMINISTRADOR', 'SUPERVISOR'],
        onError: message => this.error.set(message),
        run: credentials => this.submitSale(credentials) });
    } else await this.submitSale(null);
  }
  private async submitSale(credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.busy()) return;
    if (this.pending?.authorizerUserName && this.pending.authorizerUserName !== credentials?.userName.toUpperCase())
      throw new Error('Reintenta con el mismo usuario autorizador; la clave de esta venta conserva su identidad.');
    if (!this.pending) {
      const payments: SalePaymentInput[] = this.saleType() === 'CREDITO' ? [] : this.activePayments().flatMap(code => {
        const amount = this.paymentAmount(code);
        if (!amount) return [];
        return [{ methodCode: code, appliedAmount: amount,
          receivedAmount: code === 'EFECTIVO' ? Number(this.cashReceived) : null,
          reference: code === 'TRANSFERENCIA' ? this.transferReference.trim() || null : code === 'NEQUI' ? this.nequiReference.trim() || null : null }];
      });
      this.pending = { key: crypto.randomUUID(), authorizerUserName: credentials?.userName.toUpperCase() ?? null, input: { customerId: this.customerId.trim() || null, lines: this.cart().map(line => ({ productId: line.product.id, quantity: Number(line.quantity), priceType: line.priceType, discount: !this.generalDiscountPercent && line.discountPercent ? { type: 'PORCENTAJE', value: Number(line.discountPercent) } : null })), generalDiscount: this.generalDiscountPercent ? { type: 'PORCENTAJE', value: this.generalDiscountPercent } : null, discountReason: this.discountReason.trim() || null, payments, saleType: this.saleType(), dueDate: this.saleType() === 'CREDITO' ? this.dueDate : null, authorization: null } };
    }
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.api.confirm({ ...this.pending.input, authorization: credentials }, this.pending.key);
      this.sale.set(result); this.pending = null; this.cart.set([]); this.results.set([]); this.selectedProductId.set(null); this.cashApplied = this.cashReceived = this.transferApplied = this.nequiApplied = 0; this.generalDiscountPercent = 0; this.activePayments.set(['EFECTIVO']); this.checkoutMode.set(false);
      await this.refreshCash();
    } catch (error) { if (error instanceof HttpErrorResponse && error.status === 403 && this.pending?.input.saleType === 'CREDITO') { this.pending = null; throw new Error('No se autorizó el crédito. Verifica el rol, permiso y credenciales del autorizador.'); } if (credentials) throw error; this.error.set(`${errorText(error)} Si la respuesta se perdió, reintenta la misma venta con la misma clave.`); }
    finally { this.busy.set(false); }
  }
  async consult(): Promise<void> {
    const id = this.sale()?.id; if (!id) return;
    this.busy.set(true); this.error.set('');
    try { this.sale.set(await this.api.get(id)); } catch (error) { this.error.set(errorText(error)); }
    finally { this.busy.set(false); }
  }
  discardPending(): void { this.pending = null; this.error.set(''); }
}
