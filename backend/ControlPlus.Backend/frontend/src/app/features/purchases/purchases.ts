import { groupedNumber, NumberInput } from '../../shared/number-input';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText, reasonOr } from '../../core/api';
import { PdfFile, saveFile } from '../reports/reports-api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { CatalogApi, Category, Product } from '../catalog/catalog-api';
import { Supplier, SuppliersApi } from '../suppliers/suppliers-api';
import { Order, OrderReason, OrderSummary, OrdersApi, ReceiptLine } from './orders-api';
import { CancellationReason, Purchase, PurchaseCorrectionPreparation, PurchaseInput, PurchaseSummary, PurchasesApi } from './purchases-api';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody } from '../../shared/authorization-dialog';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PurchaseOrderDocumentPrinter } from './purchase-order-document';
import { profitPercentOf, suggestedPrice } from '../../shared/price-calculator';
import { toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';
import { InputFormat } from '../../shared/input-format';
import { foldText, SuggestField, SuggestOption } from '../../shared/suggest-field';

export function currentColombiaDate(now = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Bogota', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(now);
  const value = Object.fromEntries(parts.map(part => [part.type, part.value]));
  return `${value['year']}-${value['month']}-${value['day']}`;
}
@Component({ selector: 'cp-purchases', imports: [WindowSheet, NumberInput, InputFormat, SuggestField, CommonModule, FormsModule, MatPaginatorModule, Icon, RouterLink], templateUrl: './purchases.html', styleUrls: ['../directory.scss', './purchases.scss', './purchases-receipt.scss'] })
export class Purchases implements OnInit {
  private readonly api = inject(PurchasesApi); private readonly ordersApi = inject(OrdersApi); private readonly suppliersApi = inject(SuppliersApi); private readonly catalog = inject(CatalogApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly orderDocument = inject(PurchaseOrderDocumentPrinter);
  private readonly route = inject(ActivatedRoute, { optional: true });
  readonly session = inject(SessionService); readonly purchases = signal<Page<PurchaseSummary> | null>(null); readonly orders = signal<Page<OrderSummary> | null>(null); readonly selectedPurchase = signal<Purchase | null>(null); readonly selectedOrder = signal<Order | null>(null); readonly suppliers = signal<Supplier[]>([]); readonly products = signal<Product[]>([]); readonly reasons = signal<CancellationReason[]>([]);
  readonly loading = signal(false); readonly saving = signal(false); readonly error = toastSignal('error'); readonly notice = toastSignal('success'); readonly editing = signal(false); readonly receiving = signal(false); readonly cancelling = signal(false); readonly correctingReceipt = signal(false); readonly correctionPreparation = signal<PurchaseCorrectionPreparation | null>(null);
  readonly choosingReceipt = signal(false);
  /** Recepciones de un pedido, para verlas y corregirlas desde «Mercancía recibida» (pruebas del usuario del 24/11/2026, punto 7). */
  readonly orderReceipts = signal<{ order: OrderSummary; items: PurchaseSummary[] } | null>(null);
  readonly printingOrder = signal(false);
  readonly editingOrder = signal(false);
  readonly orderTransition = signal<'close' | 'cancel' | null>(null);
  readonly orderReasons = signal<OrderReason[]>([]);
  readonly orderConflict = signal(false);
  readonly lowStockGroups = computed(() => this.suppliers().map(supplier => ({
    supplier,
    products: this.products().filter(product => product.isActive && product.supplierId === supplier.id && product.availableStock <= product.minimumStock),
  })).filter(group => group.products.length > 0));
  orderReasonId = ''; orderObservation = '';
  orderSupplierId = '';
  orderLines: { productId: string; productName: string; productCode: string; available: number; selected: boolean; quantity: number }[] = [];
  orderDraft: Order | null = null;
  view: 'orders' | 'receipts' | 'purchases' = 'orders';
  orderSearch = ''; receiptOrderId = '';
  tab: 'purchases' | 'orders' = 'orders'; supplierId = ''; status = ''; invoiceDate = ''; supplierDocumentNumber = ''; page = 1; pageSize = 20;
  form: PurchaseInput = this.empty(); receipt: { supplierDocumentNumber: string | null; invoiceDate: string; vatMode: string; lines: ReceiptLine[] } = { supplierDocumentNumber: null, invoiceDate: currentColombiaDate(), vatMode: 'NO_DISCRIMINADO', lines: [] };
  cancelReasonId = ''; cancelObservation = ''; correctionReasonId = ''; correctionObservation = '';
  private requestId = 0;
  private pendingCancellation: { body: string; key: string } | null = null;
  private pendingCorrection: { body: string; key: string } | null = null;
  private pendingReceipt: { body: string; key: string } | null = null;
  get canOrders(): boolean { return this.session.has('PURCHASE_ORDERS.MANAGE'); }
  get canWriteOrders(): boolean { return this.canOrders && this.session.has('SUPPLIERS.READ') && this.session.has('PRODUCTS.READ') && this.session.has('INVENTORY.READ') && this.session.hasRole('SUPERVISOR', 'ADMINISTRADOR'); }
  get canReceive(): boolean { return this.session.has('PURCHASES.RECEIVE'); }
  get canChangeRetailPrice(): boolean { return this.session.hasRole('ADMINISTRADOR') && this.session.has('PRODUCTS.SENSITIVE_UPDATE'); }
  /** Al recibir un pedido, el Supervisor también pone un precio nuevo, con la autorización del Administrador (D5, 25/11/2026). */
  get canSetReceiptPrice(): boolean { return this.canChangeRetailPrice || this.session.hasRole('SUPERVISOR'); }
  /** Líneas de la recepción con «Mismo precio» desmarcado. */
  newPrice: Record<string, boolean> = {};
  /** Producto creado sin precio desde un pedido (punto 17): se recibe poniéndole su precio de venta. */
  hasNoPrice(productId: string): boolean { return this.productRetailPrice(productId) === 0; }

  /**
   * Pruebas del usuario del 26/11/2026: en una compra sin pedido se crea el proveedor ahí mismo. El Administrador lo hace
   * solo; el Supervisor que recibe mercancía, con la autorización del Administrador.
   */
  get canCreateSupplier(): boolean { return this.createsSupplierAlone || (this.session.hasRole('SUPERVISOR') && this.session.has('PURCHASES.RECEIVE')); }
  private get createsSupplierAlone(): boolean { return this.session.has('SUPPLIERS.MANAGE') && this.session.hasRole('ADMINISTRADOR'); }
  readonly addingSupplier = signal(false);
  newSupplier = { name: '', documentNumber: '', phone: '' };
  startNewSupplier(): void { if (!this.canCreateSupplier) return; this.newSupplier = { name: '', documentNumber: '', phone: '' }; this.addingSupplier.set(true); }
  async createSupplier(): Promise<void> {
    if (!this.canCreateSupplier || this.saving()) return;
    const name = this.newSupplier.name.trim(); const document = this.newSupplier.documentNumber.trim();
    if (!name) { this.error.set('Escribe el nombre del proveedor.'); return; }
    const input = { name, documentType: document ? 'NIT' : null, documentNumber: document || null,
      phone: this.newSupplier.phone.trim() || null, address: null, email: null, observations: null };
    this.error.set('');
    if (!this.createsSupplierAlone) {
      await this.authorization.open({ title: 'Autorizar proveedor nuevo',
        description: 'El Administrador autoriza crear este proveedor (su carné, o su usuario y contraseña).',
        detail: name, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
        onError: message => this.error.set(message),
        run: async credentials => {
          this.saving.set(true);
          try { this.supplierCreated(await this.suppliersApi.create(input, credentials ? authorizationBody(credentials) : undefined)); }
          catch (e) { throw new Error(reasonOr(e, 'No se autorizó el proveedor nuevo. Lo autoriza un Administrador activo.')); }
          finally { this.saving.set(false); }
        } });
      return;
    }
    this.saving.set(true);
    try { this.supplierCreated(await this.suppliersApi.create(input)); }
    catch (e) { this.error.set(reasonOr(e, 'No se pudo crear el proveedor.')); }
    finally { this.saving.set(false); }
  }
  private supplierCreated(supplier: Supplier): void {
    this.suppliers.update(list => [...list, supplier].sort((a, b) => a.name.localeCompare(b.name, 'es')));
    this.form.supplierId = supplier.id; this.addingSupplier.set(false);
    this.notice.set(`Proveedor ${supplier.name} creado y elegido para esta compra.`);
  }
  setSamePrice(line: ReceiptLine, same: boolean): void {
    this.newPrice = { ...this.newPrice, [line.orderLineId]: !same };
    line.newRetailPrice = same ? null : this.productRetailPrice(line.productId) || null;
    if (!same) setTimeout(() => (document.getElementById('retail-price-' + line.orderLineId) as HTMLInputElement | null)?.focus());
  }
  get canCancelOrder(): boolean { return this.canOrders && this.session.has('PURCHASE_ORDERS.CANCEL') && this.session.hasRole('ADMINISTRADOR'); }
  get canCloseOrder(): boolean { return this.canOrders && this.session.hasRole('SUPERVISOR', 'ADMINISTRADOR'); }
  get orderPendingTotal(): number { return this.selectedOrder()?.lines.reduce((sum, line) => sum + line.pendingQuantity, 0) ?? 0; }
  get orderReceivedTotal(): number { return this.selectedOrder()?.lines.reduce((sum, line) => sum + line.receivedQuantity, 0) ?? 0; }
  get canStartOrderCancel(): boolean { const order = this.selectedOrder(); return this.canCancelOrder && !!order && (order.status === 'BORRADOR' || order.status === 'ENVIADO') && this.orderReceivedTotal === 0; }
  get canStartOrderClose(): boolean { const order = this.selectedOrder(); return this.canCloseOrder && !!order && (order.status === 'ENVIADO' || order.status === 'RECIBIDO_PARCIAL') && this.orderPendingTotal > 0; }
  get orderReasonIsOther(): boolean { return this.orderReasons().find(reason => reason.id === this.orderReasonId)?.code === 'OTRO'; }
  get canCancel(): boolean { return this.session.has('PURCHASES.CANCEL') && this.session.hasRole('ADMINISTRADOR'); }
  /** Auditoría S.20: el Supervisor que recibe mercancía también corrige una recepción, con la autorización del Administrador. */
  get canCorrectReceipt(): boolean { return this.canCancel || (this.session.has('PURCHASES.RECEIVE') && this.session.hasRole('SUPERVISOR')); }
  get correctionReasonIsOther(): boolean { return this.reasons().find(reason => reason.id === this.correctionReasonId)?.code === 'OTRO'; }
  ngOnInit(): void {
    const configuredView = this.route?.snapshot.data['purchaseView'];
    this.view = configuredView === 'receipts' ? 'receipts' : this.canOrders ? 'orders' : 'purchases';
    this.tab = this.view === 'purchases' ? 'purchases' : 'orders';
    void this.initialize();
    // Desde Inicio, «Recibir mercancía» abre la pregunta de «Registrar compra».
    if (this.route?.snapshot.queryParamMap?.get('accion') === 'registrar') this.startRegisterPurchase();
  }
  private empty(): PurchaseInput { return { supplierId: '', supplierDocumentNumber: null, invoiceDate: currentColombiaDate(), vatMode: 'NO_DISCRIMINADO', lines: [] }; }
  private async initialize(): Promise<void> {
    await this.reload();
    if (this.session.has('SUPPLIERS.READ')) { try { this.suppliers.set(await this.suppliersApi.active()); } catch (e) { this.error.set(errorText(e)); } }
    if (this.session.has('PRODUCTS.READ') && this.session.has('INVENTORY.READ')) await this.loadProducts();
  }
  supplierName(id: string): string { return this.suppliers().find(s => s.id === id)?.name ?? 'Proveedor'; }
  productName(id: string): string { return this.products().find(p => p.id === id)?.name ?? this.selectedOrder()?.lines.find(l => l.productId === id)?.productName ?? 'Producto'; }
  /** El precio nuevo viene escrito con el actual (auditoría del 23/11/2026); solo se envía si de verdad cambió. */
  changedPrice(productId: string, price: number | null): number | null {
    if (price == null || `${price}` === '') return null;
    return Number(price) === this.productRetailPrice(productId) ? null : Number(price);
  }
  productRetailPrice(id: string): number | null { return this.products().find(p => p.id === id)?.retailPrice ?? null; }
  async reload(): Promise<void> { const id = ++this.requestId; this.loading.set(true); this.error.set(''); try { if (this.tab === 'purchases' && this.canReceive) { const result = await this.api.list({ supplierId: this.supplierId, status: this.status, invoiceDate: this.invoiceDate, supplierDocumentNumber: this.supplierDocumentNumber.trim(), page: this.page, pageSize: this.pageSize }); if (id === this.requestId) this.purchases.set(result); } else if (this.tab === 'orders' && this.canOrders) { const result = await this.ordersApi.list({ supplierId: this.supplierId, status: this.status, page: this.page, pageSize: this.pageSize }); const items = await this.hydrateOrderSummaries(result.items); if (id === this.requestId) this.orders.set({ ...result, items }); } } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); } finally { if (id === this.requestId) this.loading.set(false); } }
  private async hydrateOrderSummaries(items: OrderSummary[]): Promise<OrderSummary[]> { return Promise.all(items.map(async item => { if (Array.isArray(item.lines)) return item; const order = await this.ordersApi.get(item.id); return { ...item, lines: order.lines.map(line => ({ productCode: line.productCode, productName: line.productName, requestedQuantity: line.requestedQuantity, receivedQuantity: line.receivedQuantity })) }; })); }
  /** Compras más claras (decisión del 19 de noviembre de 2026): una sola pantalla con pestañas y un solo «Registrar compra». */
  readonly askingPurchaseKind = signal(false);
  showView(view: 'orders' | 'receipts' | 'purchases'): void {
    if (view === 'purchases') { this.switchTab('purchases'); return; }
    this.switchTab('orders');
    this.view = view;
  }
  /** Propuesta 3 (21 de noviembre de 2026): «Recibir mercancía» es el botón principal y «Nuevo pedido al proveedor» va a su lado. */
  startRegisterPurchase(): void { if (this.canReceive) { this.error.set(''); this.askingPurchaseKind.set(true); } }
  async registerFromOrder(): Promise<void> {
    this.askingPurchaseKind.set(false);
    if (this.view === 'purchases') { this.showView('receipts'); await this.reload(); }
    this.startReceiptSelection();
  }
  async registerWithoutOrder(): Promise<void> {
    this.askingPurchaseKind.set(false);
    if (this.view !== 'purchases') this.showView('purchases');
    await this.newPurchase();
  }
  switchTab(tab: 'purchases' | 'orders'): void { this.tab = tab; this.view = tab === 'purchases' ? 'purchases' : 'orders'; this.status = ''; this.page = 1; this.selectedOrder.set(null); this.selectedPurchase.set(null); void this.reload(); }
  showDirectPurchases(): void { this.switchTab('purchases'); }
  showOrders(): void { this.switchTab('orders'); }
  openOrderForSupplier(supplierId: string): void { void this.newOrder().then(() => this.setOrderSupplier(supplierId)); }
  requestedTotal(order: OrderSummary): number { return order.lines?.reduce((sum, line) => sum + line.requestedQuantity, 0) ?? 0; }
  async showReceipts(order: OrderSummary): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true); this.error.set('');
    try { this.orderReceipts.set({ order, items: (await this.api.list({ orderId: order.id, pageSize: 100 })).items }); }
    catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }
  /** Abre el detalle de una recepción, donde están «Imprimir etiquetas» y «Corregir recepción». */
  async openReceipt(purchase: PurchaseSummary): Promise<void> { this.orderReceipts.set(null); await this.detail(purchase); }
  receivedTotal(order: OrderSummary): number { return order.lines?.reduce((sum, line) => sum + line.receivedQuantity, 0) ?? 0; }
  pendingTotal(order: OrderSummary): number { return this.requestedTotal(order) - this.receivedTotal(order); }
  /** Solo se ofrece «Recibir mercancía» cuando el pedido tiene algo por recibir (no un botón apagado al lado de «Ver recepciones»). */
  canReceiveOrder(order: OrderSummary): boolean { return this.canReceive && (order.status === 'ENVIADO' || order.status === 'RECIBIDO_PARCIAL') && this.pendingTotal(order) > 0; }
  async receiveOrder(order: OrderSummary): Promise<void> { await this.orderDetail(order); if (this.selectedOrder()?.id === order.id) this.startReceipt(); }
  filteredOrders(): OrderSummary[] {
    const term = this.orderSearch.trim().toLocaleLowerCase('es');
    const items = this.orders()?.items ?? [];
    if (!term) return items;
    return items.filter(order => order.visibleNumber.toLocaleLowerCase('es').includes(term)
      || this.supplierName(order.supplierId).toLocaleLowerCase('es').includes(term)
      || order.status.toLocaleLowerCase('es').includes(term)
      || order.lines?.some(line => line.productName.toLocaleLowerCase('es').includes(term)
        || line.productCode.toLocaleLowerCase('es').includes(term)));
  }
  receivableOrders(): OrderSummary[] { return (this.orders()?.items ?? []).filter(order => (order.status === 'ENVIADO' || order.status === 'RECIBIDO_PARCIAL') && this.pendingTotal(order) > 0); }
  startReceiptSelection(): void { this.receiptOrderId = this.receivableOrders()[0]?.id ?? ''; this.choosingReceipt.set(true); }
  async confirmReceiptSelection(): Promise<void> { const order = this.receivableOrders().find(item => item.id === this.receiptOrderId); if (!order) { this.error.set('Selecciona un pedido con cantidades pendientes.'); return; } this.choosingReceipt.set(false); await this.receiveOrder(order); }
  statusLabel(status: string): string { return ({ BORRADOR: 'Borrador', ENVIADO: 'Pendiente', RECIBIDO_PARCIAL: 'Parcial', COMPLETADO: 'Recibido', CERRADO_CON_PENDIENTES: 'Cerrado', CANCELADO: 'Anulado', CONFIRMADA: 'Confirmada', ANULADA: 'Anulada' } as Record<string, string>)[status] ?? status; }
  find(): void { this.page = 1; void this.reload(); }
  pageChanged(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  async detail(p: PurchaseSummary): Promise<void> { this.loading.set(true); this.error.set(''); try { this.selectedPurchase.set(await this.api.get(p.id)); if (this.session.has('PRODUCTS.READ')) await this.loadProducts(); } catch (e) { this.error.set(errorText(e)); } finally { this.loading.set(false); } }
  async orderDetail(o: OrderSummary): Promise<void> { this.loading.set(true); this.error.set(''); try { this.selectedOrder.set(await this.ordersApi.get(o.id)); this.orderConflict.set(false); } catch (e) { this.error.set(errorText(e)); } finally { this.loading.set(false); } }
  async refreshOrder(): Promise<void> { const order = this.selectedOrder(); if (!order || this.loading() || this.saving()) return; this.loading.set(true); try { this.selectedOrder.set(await this.ordersApi.get(order.id)); this.orderConflict.set(false); this.error.set(''); } catch (e) { this.error.set(errorText(e)); } finally { this.loading.set(false); } }
  async startOrderTransition(kind: 'close' | 'cancel'): Promise<void> { if (this.saving() || this.orderConflict() || (kind === 'close' ? !this.canStartOrderClose : !this.canStartOrderCancel)) return; this.error.set(''); try { const reasons = kind === 'close' ? await this.ordersApi.closeReasons() : await this.ordersApi.cancelReasons(); if (!reasons.length) { this.error.set('No hay motivos activos para esta operación. Solicita su configuración al Administrador.'); return; } this.orderReasons.set(reasons); this.orderReasonId = ''; this.orderObservation = ''; this.orderTransition.set(kind); } catch (e) { this.error.set(errorText(e)); } }
  closeOrderTransition(): void { if (this.saving()) return; this.orderTransition.set(null); }
  orderTransitionValidation(): string { const kind = this.orderTransition(); const reason = this.orderReasons().find(r => r.id === this.orderReasonId); if (!kind || !(kind === 'close' ? this.canStartOrderClose : this.canStartOrderCancel)) return 'El pedido ya no permite esta operación. Actualiza el detalle.'; if (!reason) return 'Selecciona un motivo activo.'; if (reason.code === 'OTRO' && !this.orderObservation.trim()) return 'Describe el motivo en la observación.'; if (this.orderObservation.length > 500) return 'La observación supera 500 caracteres.'; return ''; }
  async submitOrderTransition(): Promise<void> { const kind = this.orderTransition(); const order = this.selectedOrder(); if (!kind || !order || this.saving() || this.orderConflict()) return; const issue = this.orderTransitionValidation(); if (issue) { this.error.set(issue); return; } await this.authorization.open({ title: kind === 'close' ? 'Autorizar cierre de pedido' : 'Autorizar anulación de pedido', description: kind === 'close' ? `Quedarán ${groupedNumber(this.orderPendingTotal)} unidades pendientes sin recibir; no se registrará mercancía.` : 'El pedido quedará anulado en el historial sin revertir inventario.', detail: `Pedido ${order.visibleNumber}`, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: false, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message), run: credentials => this.executeOrderTransition(kind, order, credentials) }); }
  private async executeOrderTransition(kind: 'close' | 'cancel', order: Order, credentials: AuthorizationCredentials | null): Promise<void> { if (this.saving() || this.orderConflict()) throw new Error('Actualiza el pedido antes de reintentar.'); this.saving.set(true); this.error.set(''); try { const input = { reasonId: this.orderReasonId, observation: this.orderObservation.trim() || null, ...(credentials ? { authorization: authorizationBody(credentials) } : {}) }; const result = kind === 'close' ? await this.ordersApi.close(order, input) : await this.ordersApi.cancel(order, input); this.selectedOrder.set(result); this.orderTransition.set(null); this.notice.set(kind === 'close' ? 'Pedido cerrado con pendientes. No se registró recepción.' : 'Pedido anulado. Se conserva su historial.'); await this.reload(); } catch (e) { if (this.isConflict(e)) this.orderConflict.set(true); this.error.set(errorText(e)); throw e; } finally { this.saving.set(false); } }
  private isConflict(error: unknown): boolean { return !!error && typeof error === 'object' && 'status' in error && error.status === 409; }
  async newOrder(): Promise<void> { if (!this.canWriteOrders) return; this.orderDraft = null; this.newOrderProduct.set(null); this.orderSupplierId = ''; this.orderLines = []; this.error.set(''); this.editingOrder.set(true); await this.loadProducts(); }
  async editOrder(order: Order): Promise<void> { if (!this.canWriteOrders || order.status !== 'BORRADOR') return; this.orderDraft = order; this.orderSupplierId = order.supplierId; this.error.set(''); this.editingOrder.set(true); await this.loadProducts(); this.setOrderSupplier(order.supplierId); for (const line of order.lines) { const item = this.orderLines.find(x => x.productId === line.productId); if (item) { item.selected = true; item.quantity = line.requestedQuantity; } } }
  /** Punto 17 (24/11/2026): pedir un producto que aún no está en el sistema. Se crea en Catálogo desde el pedido; el precio de venta es opcional. */
  readonly newOrderProduct = signal<{ name: string; categoryId: string; retailPrice: number | null; quantity: number; key: string } | null>(null);
  readonly orderCategories = signal<Category[]>([]);
  private orderUnitId = '';
  get canCreateOrderProduct(): boolean { return this.canWriteOrders && this.session.has('PRODUCTS.CREATE'); }
  async startOrderProduct(): Promise<void> {
    if (!this.canCreateOrderProduct) return;
    if (!this.orderSupplierId) { this.error.set('Elige primero el proveedor.'); return; }
    this.error.set('');
    if (await this.loadNewProductOptions()) this.newOrderProduct.set({ name: '', categoryId: '', retailPrice: null, quantity: 1, key: crypto.randomUUID() });
  }
  /** Categorías activas y la unidad «Unidad» para crear un producto desde un pedido o una compra. */
  private async loadNewProductOptions(): Promise<boolean> {
    try {
      if (!this.orderCategories().length) this.orderCategories.set((await this.catalog.categories({ page: 1, pageSize: 100 })).items.filter(c => c.isActive));
      if (!this.orderUnitId) { const units = await this.catalog.units(); this.orderUnitId = (units.find(u => u.code === 'UNIDAD') ?? units[0])?.id ?? ''; }
      return true;
    } catch (e) { this.error.set(errorText(e)); return false; }
  }
  /**
   * Pruebas del usuario del 26/11/2026: en la compra sin pedido también se crea el producto que aún no está en el
   * sistema. La mercancía llega ya, así que aquí el precio de venta es obligatorio; queda en Catálogo con el proveedor de
   * la compra, sin existencias: las pone la compra al confirmarla.
   */
  readonly newPurchaseProduct = signal<{ name: string; categoryId: string; retailPrice: number | null; quantity: number; unitCost: number | null; key: string } | null>(null);
  get canCreatePurchaseProduct(): boolean { return this.canReceive && this.session.has('PRODUCTS.CREATE'); }
  async startPurchaseProduct(): Promise<void> {
    if (!this.canCreatePurchaseProduct) return;
    if (!this.form.supplierId) { this.error.set('Elige primero el proveedor.'); return; }
    this.error.set('');
    if (await this.loadNewProductOptions()) this.newPurchaseProduct.set({ name: '', categoryId: '', retailPrice: null, quantity: 1, unitCost: null, key: crypto.randomUUID() });
  }
  async addPurchaseProduct(): Promise<void> {
    const draft = this.newPurchaseProduct(); if (!draft || this.saving()) return;
    const name = draft.name.trim(), quantity = Number(draft.quantity), price = Number(draft.retailPrice), cost = Number(draft.unitCost);
    if (!name) { this.error.set('Escribe el nombre del producto nuevo.'); return; }
    if (!draft.categoryId) { this.error.set('Elige la categoría del producto nuevo.'); return; }
    if (!Number.isInteger(price) || price <= 0) { this.error.set('Escribe el precio de venta en pesos enteros: el producto queda listo para vender al confirmar la compra.'); return; }
    if (!Number.isInteger(quantity) || quantity <= 0) { this.error.set('La cantidad que llegó debe ser un número entero mayor que cero.'); return; }
    if (!(cost > 0)) { this.error.set('Escribe el costo por unidad del producto nuevo.'); return; }
    this.saving.set(true); this.error.set('');
    try {
      const product = await this.catalog.createProduct({ name, categoryId: draft.categoryId, measurementUnitId: this.orderUnitId, supplierId: this.form.supplierId,
        barcode: null, barcodeFormat: null, description: null, retailPrice: price, wholesalePrice: null, taxPercentage: null, minimumStock: 0,
        initialQuantity: 0, initialUnitCost: null }, true, draft.key);
      this.products.update(list => [...list, product]);
      this.form.lines = [...this.form.lines, { productId: product.id, quantity, unitCost: cost, vatPercentage: null, newRetailPrice: null }];
      this.newPurchaseProduct.set(null);
      this.notice.set(`${product.name} quedó en Catálogo y en la compra. Sus existencias se suman al confirmar la compra.`);
    } catch (e) { this.error.set(reasonOr(e, 'No se pudo crear el producto nuevo. Revisa los datos e inténtalo de nuevo.')); }
    finally { this.saving.set(false); }
  }
  async addOrderProduct(): Promise<void> {
    const draft = this.newOrderProduct(); if (!draft || this.saving()) return;
    const name = draft.name.trim(), quantity = Number(draft.quantity);
    const price = draft.retailPrice === null || (draft.retailPrice as unknown) === '' ? 0 : Number(draft.retailPrice);
    if (!name) { this.error.set('Escribe el nombre del producto nuevo.'); return; }
    if (!draft.categoryId) { this.error.set('Elige la categoría del producto nuevo.'); return; }
    if (!Number.isInteger(price) || price < 0) { this.error.set('El precio de venta va en pesos enteros, o déjalo vacío para ponerlo después.'); return; }
    if (!Number.isInteger(quantity) || quantity <= 0) { this.error.set('La cantidad a pedir debe ser un número entero mayor que cero.'); return; }
    this.saving.set(true); this.error.set('');
    try {
      const product = await this.catalog.createProduct({ name, categoryId: draft.categoryId, measurementUnitId: this.orderUnitId, supplierId: this.orderSupplierId,
        barcode: null, barcodeFormat: null, description: null, retailPrice: price, wholesalePrice: null, taxPercentage: null, minimumStock: 0,
        initialQuantity: 0, initialUnitCost: null }, true, draft.key);
      this.products.update(list => [...list, product]);
      this.orderLines = [...this.orderLines, { productId: product.id, productName: product.name, productCode: product.internalCode, available: 0, selected: true, quantity }];
      this.newOrderProduct.set(null);
      this.notice.set(price > 0 ? `${product.name} quedó en Catálogo y en el pedido.`
        : `${product.name} quedó en Catálogo y en el pedido. Todavía no tiene precio de venta: ponlo al recibirlo o en Catálogo; mientras tanto no se puede vender.`);
    } catch (e) { this.error.set(reasonOr(e, 'No se pudo crear el producto nuevo. Revisa los datos e inténtalo de nuevo.')); }
    finally { this.saving.set(false); }
  }
  /**
   * Pruebas del usuario del 26/11/2026: en el pedido se busca el producto escribiendo o con el lector. La lista se filtra
   * mientras se escribe; con Enter se incluye el producto (si ya estaba incluido, suma uno a la cantidad).
   */
  orderProductSearch = '';
  readonly orderSearchNote = signal('');
  visibleOrderLines(): Purchases['orderLines'] {
    const words = foldText(this.orderProductSearch).split(/\s+/).filter(Boolean);
    if (!words.length) return this.orderLines;
    return this.orderLines.filter(line => { const text = this.productSearchText(line.productId, line.productName, line.productCode); return words.every(word => text.includes(word)); });
  }
  pickOrderProduct(): void {
    const term = foldText(this.orderProductSearch);
    if (!term) return;
    const visible = this.visibleOrderLines();
    const line = visible.find(item => this.productKeys(item.productId, item.productCode).includes(term)) ?? (visible.length === 1 ? visible[0] : undefined);
    if (!line) {
      const other = this.products().find(product => this.productKeys(product.id, product.internalCode).includes(term));
      this.orderSearchNote.set(other
        ? `${other.name} no está asignado a este proveedor. Cámbiale el proveedor en Catálogo o pídelo a su proveedor.`
        : visible.length ? 'Hay varios productos con eso: elige el tuyo en la lista y marca «Incluir».' : `No hay productos de este proveedor con «${this.orderProductSearch.trim()}».`);
      return;
    }
    if (line.selected) line.quantity = (Number(line.quantity) || 0) + 1; else line.selected = true;
    this.orderSearchNote.set(`Incluido: ${line.productName} · cantidad ${groupedNumber(line.quantity)}.`);
    this.orderProductSearch = '';
  }
  /** Nombre, referencia y código de barras sin tildes, para buscar. */
  private productSearchText(productId: string, name: string, code: string): string {
    return foldText(`${name} ${code} ${this.products().find(product => product.id === productId)?.barcode ?? ''}`);
  }
  /** Referencia y código de barras exactos (lo que manda el lector). */
  private productKeys(productId: string, code: string): string[] {
    const barcode = this.products().find(product => product.id === productId)?.barcode;
    return [code, barcode].filter((key): key is string => !!key).map(foldText);
  }
  /** Pruebas del usuario del 26/11/2026: en la compra sin pedido el producto se busca escribiendo o con el lector. */
  purchaseProductSearch = '';
  readonly purchaseProductOptions = computed<SuggestOption[]>(() => this.products().filter(product => product.isActive)
    .map(product => ({ value: product.id, text: `${product.name} · ${product.internalCode}`, hint: product.barcode ?? undefined,
      keys: [product.internalCode, product.barcode].filter((key): key is string => !!key) })));
  /** Agrega el producto como línea; si ya está en la compra, suma uno a la cantidad. El costo viene con el actual. */
  pickPurchaseProduct(productId: string): void {
    if (!productId) return;
    const product = this.products().find(item => item.id === productId);
    const existing = this.form.lines.find(line => line.productId === productId);
    if (existing) existing.quantity = (Number(existing.quantity) || 0) + 1;
    else this.form.lines = [...this.form.lines, { productId, quantity: 1, unitCost: product?.averageCost ?? 0, vatPercentage: null, newRetailPrice: null }];
    this.purchaseProductSearch = '';
  }
  productCode(id: string): string { return this.products().find(product => product.id === id)?.internalCode ?? ''; }
  setOrderSupplier(id: string): void { this.orderSupplierId = id; this.orderProductSearch = ''; this.orderSearchNote.set(''); this.orderLines = this.products().filter(p => p.isActive && p.supplierId === id).map(p => ({ productId: p.id, productName: p.name, productCode: p.internalCode, available: p.availableStock, selected: false, quantity: 1 })); }
  orderValidation(): string { if (!this.orderSupplierId || !this.suppliers().some(s => s.id === this.orderSupplierId && s.isActive)) return 'Selecciona un proveedor activo.'; const lines = this.orderLines.filter(l => l.selected); if (!lines.length || lines.length > 200) return 'Incluye entre 1 y 200 productos.'; if (lines.some(l => !Number.isInteger(l.quantity) || l.quantity <= 0)) return 'Indica cantidades enteras positivas.'; return ''; }
  async saveOrder(): Promise<void> { if (!this.canWriteOrders || this.saving()) return; const issue = this.orderValidation(); if (issue) { this.error.set(issue); return; } this.saving.set(true); this.error.set(''); try { const lines = this.orderLines.filter(l => l.selected).map(l => ({ productId: l.productId, requestedQuantity: l.quantity })); const result = this.orderDraft ? await this.ordersApi.update(this.orderDraft, lines) : await this.ordersApi.create({ supplierId: this.orderSupplierId, lines }); this.selectedOrder.set(result); this.editingOrder.set(false); this.notice.set('Pedido guardado.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async sendOrder(): Promise<void> { const order = this.selectedOrder(); if (!order || order.status !== 'BORRADOR' || !this.canWriteOrders || this.saving()) return; this.saving.set(true); this.error.set(''); try { const sent = await this.ordersApi.send(order); this.selectedOrder.set(sent); this.notice.set('Pedido enviado. Descargando el PDF…'); await this.reload(); await this.downloadOrderPdf(sent, true); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async printOrder(order: Order | null = this.selectedOrder(), generatedAfterSend = false): Promise<void> { if (!order || this.printingOrder()) return; this.printingOrder.set(true); this.error.set(''); try { const supplier = this.suppliers().find(item => item.id === order.supplierId) ?? await this.suppliersApi.get(order.supplierId); await this.orderDocument.print(order, supplier, this.session.user()?.displayName ?? ''); this.notice.set(generatedAfterSend ? 'Pedido enviado y documento generado.' : 'Documento del pedido listo para imprimir o guardar como PDF.'); } catch (e) { this.error.set(generatedAfterSend ? `El pedido fue enviado, pero no se pudo abrir el documento. ${errorText(e)}` : errorText(e)); } finally { this.printingOrder.set(false); } }
  /** Auditoría S.19: al enviar, el pedido se descarga solo como PDF; desde el detalle se puede volver a descargar o imprimir. */
  async downloadOrderPdf(order: Order | null = this.selectedOrder(), afterSend = false): Promise<void> {
    if (!order || this.printingOrder()) return;
    this.printingOrder.set(true); if (!afterSend) this.error.set('');
    try {
      this.saveDownloaded(await this.ordersApi.documentPdf(order));
      this.notice.set(afterSend ? 'Pedido enviado. Se descargó el PDF; también puedes imprimirlo desde el pedido.' : 'PDF del pedido descargado.');
    } catch (e) { this.error.set(afterSend ? `El pedido fue enviado, pero no se pudo descargar el PDF. ${reasonOr(e, 'Vuelve a intentarlo desde el pedido.')}` : reasonOr(e, 'No se pudo descargar el PDF del pedido.')); }
    finally { this.printingOrder.set(false); }
  }
  /** Entrega el archivo al navegador (aparte para poder probarlo). */
  saveDownloaded(file: PdfFile): void { saveFile(file); }
  async newPurchase(): Promise<void> { if (!this.canReceive) return; this.selectedPurchase.set(null); this.form = this.empty(); this.editing.set(true); await this.loadProducts(); }
  async editPurchase(p: Purchase): Promise<void> { if (!this.canReceive || p.status !== 'BORRADOR') return; this.form = { supplierId: p.supplierId, supplierDocumentNumber: p.supplierDocumentNumber, invoiceDate: p.invoiceDate, vatMode: 'NO_DISCRIMINADO', lines: p.lines.map(l => ({ productId: l.productId, quantity: l.quantity, unitCost: l.unitCost, vatPercentage: null, newRetailPrice: l.newRetailPrice })) }; this.editing.set(true); await this.loadProducts(); }
  private async loadProducts(): Promise<void> { try { const all: Product[] = []; let page = 1, total = 1; do { const result = await this.catalog.products({ includeOutOfStock: true, page, pageSize: 100 }); all.push(...result.items); total = result.totalPages; page++; } while (page <= total); this.products.set(all); } catch (e) { this.error.set(errorText(e)); } }
  /** Porcentaje de ganancia escrito en la calculadora, por producto (no viaja al servidor). */
  profit: Record<string, number | null> = {};
  /** Costo con el que se comparó cada línea al empezar la recepción, para avisar si subió o bajó. */
  previousCost: Record<string, number | null> = {};
  requestedFor(orderLineId: string): number { return this.selectedOrder()?.lines.find(l => l.id === orderLineId)?.requestedQuantity ?? 0; }
  /** Botones − y + de «¿Cuántas llegaron?», entre 0 y lo pendiente. */
  stepReceived(line: ReceiptLine, delta: number): void {
    line.quantity = Math.min(Math.max((Number(line.quantity) || 0) + delta, 0), this.pendingFor(line.orderLineId));
  }
  /** «Subió: antes $ 6.000», «Bajó: …» o «Igual que antes». */
  costHint(line: ReceiptLine): { text: string; up: boolean } | null {
    const before = this.previousCost[line.orderLineId]; const now = Number(line.unitCost);
    if (before == null || !(before > 0) || !(now > 0)) return null;
    if (now === before) return { text: 'Igual que antes', up: false };
    return { text: `${now > before ? 'Subió' : 'Bajó'}: antes $ ${groupedNumber(before)}`, up: now > before };
  }
  /** Pruebas del usuario del 26/11/2026: con el % de ganancia escrito, el precio nuevo se calcula solo, también si cambia el costo. */
  refreshPrice(line: ReceiptLine): void {
    if (!this.newPrice[line.orderLineId]) return;
    const percent = this.profit[line.orderLineId];
    if (percent == null || `${percent}` === '') return;
    const price = suggestedPrice(line.unitCost, percent);
    if (price !== null) line.newRetailPrice = price;
  }
  /** Lo que se está recibiendo: productos, unidades, valor y cuántos precios cambian. */
  receiptTotals(): { products: number; units: number; total: number; priceChanges: number } {
    const lines = this.receipt.lines.filter(l => Number(l.quantity) > 0);
    return { products: lines.length, units: lines.reduce((sum, l) => sum + Number(l.quantity), 0),
      total: Math.round(lines.reduce((sum, l) => sum + Number(l.quantity) * (Number(l.unitCost) || 0), 0)),
      priceChanges: lines.filter(l => this.newPrice[l.orderLineId] && this.changedPrice(l.productId, l.newRetailPrice) != null).length };
  }
  /** Pone en «precio nuevo» el precio que deja la ganancia indicada sobre el costo de la línea. */
  applyProfit(key: string, line: { unitCost: number; newRetailPrice: number | null }): void {
    const price = suggestedPrice(line.unitCost, this.profit[key]);
    if (price === null) { this.error.set('Indica el costo del producto y el % de ganancia para calcular el precio.'); return; }
    this.error.set(''); line.newRetailPrice = price;
  }
  /** Cuánto se gana sobre el costo con el precio nuevo o, si se deja en blanco, con el precio actual. */
  profitNote(line: { productId: string; unitCost: number; newRetailPrice: number | null }): { text: string; low: boolean } | null {
    const custom = line.newRetailPrice != null && `${line.newRetailPrice}` !== '';
    const percent = profitPercentOf(custom ? line.newRetailPrice : this.productRetailPrice(line.productId), line.unitCost);
    if (percent === null) return null;
    if (percent < 0) return { text: custom ? 'Por debajo del costo' : 'El precio actual está por debajo del costo', low: true };
    return { text: custom ? `Ganas ${groupedNumber(percent)} % sobre el costo` : `Con el precio actual ganas ${groupedNumber(percent)} %`, low: false };
  }
  removeLine(index: number): void { this.form.lines.splice(index, 1); }
  purchaseValidation(): string { const f = this.form; if (!f.supplierId || !this.suppliers().some(s => s.id === f.supplierId && s.isActive)) return 'Selecciona un proveedor activo.'; if (!f.invoiceDate) return 'Indica la fecha de factura.'; if ((f.supplierDocumentNumber?.trim().length ?? 0) > 100) return 'El número de factura supera 100 caracteres.'; if (!f.lines.length || f.lines.length > 200) return 'Incluye entre 1 y 200 productos.'; if (new Set(f.lines.map(l => l.productId)).size !== f.lines.length) return 'Cada producto debe aparecer una sola vez.'; for (const l of f.lines) { if (!l.productId || !Number.isInteger(l.quantity) || l.quantity <= 0) return 'Indica producto y cantidad entera positiva.'; if (!Number.isFinite(l.unitCost) || l.unitCost <= 0 || Math.round(l.unitCost * 10000) !== l.unitCost * 10000) return 'Indica un costo positivo de hasta cuatro decimales.'; if (this.hasNoPrice(l.productId) && !(Number(l.newRetailPrice) > 0)) return this.canChangeRetailPrice ? `${this.productName(l.productId)} todavía no tiene precio de venta: escríbelo en «Precio nuevo».` : `${this.productName(l.productId)} todavía no tiene precio de venta: pídele al Administrador que lo reciba y le ponga el precio.`; if (l.newRetailPrice != null && (!Number.isFinite(l.newRetailPrice) || l.newRetailPrice <= 0 || !Number.isInteger(l.newRetailPrice))) return 'El precio nuevo debe ser un valor entero mayor que cero.'; if (l.vatPercentage != null && (!Number.isFinite(l.vatPercentage) || l.vatPercentage < 0 || l.vatPercentage > 100 || Math.round(l.vatPercentage * 100) !== l.vatPercentage * 100)) return 'Revisa el IVA de las líneas.'; if (f.vatMode === 'NO_DISCRIMINADO' && l.vatPercentage != null) return 'Este modo de IVA no admite porcentaje por producto.'; } return ''; }
  async savePurchase(): Promise<void> { if (this.saving() || !this.canReceive) return; const issue = this.purchaseValidation(); if (issue) { this.error.set(issue); return; } this.saving.set(true); this.error.set(''); try { const selected = this.selectedPurchase(); const input = { ...this.form, supplierDocumentNumber: this.form.supplierDocumentNumber?.trim() || null, lines: this.form.lines.map(l => ({ ...l, vatPercentage: this.form.vatMode === 'NO_DISCRIMINADO' ? null : l.vatPercentage })) }; const result = selected ? await this.api.update(selected, input) : await this.api.create(input); this.selectedPurchase.set(result); this.editing.set(false); this.notice.set('Borrador guardado.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async confirmPurchase(): Promise<void> { const p = this.selectedPurchase(); if (!p || p.status !== 'BORRADOR' || this.saving() || !this.canReceive) return; this.saving.set(true); this.error.set(''); const key = crypto.randomUUID(); try { this.selectedPurchase.set(await this.api.confirm(p, key)); this.notice.set('Compra confirmada.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  startReceipt(): void { const order = this.selectedOrder(); if (!order || !this.canReceive || this.orderConflict() || (order.status !== 'ENVIADO' && order.status !== 'RECIBIDO_PARCIAL') || !order.lines.some(l => l.pendingQuantity > 0)) return; this.receipt = { supplierDocumentNumber: null, invoiceDate: currentColombiaDate(), vatMode: 'NO_DISCRIMINADO', lines: order.lines.filter(l => l.pendingQuantity > 0).map(l => ({ orderLineId: l.id, productId: l.productId, quantity: l.pendingQuantity, unitCost: l.estimatedUnitCost ?? l.currentUnitCost ?? 0, vatPercentage: null, newRetailPrice: null })) }; this.profit = {}; this.previousCost = Object.fromEntries(order.lines.map(l => [l.id, l.currentUnitCost ?? l.estimatedUnitCost ?? null])); this.newPrice = Object.fromEntries(this.receipt.lines.filter(l => this.hasNoPrice(l.productId)).map(l => [l.orderLineId, true])); this.receiving.set(true); }
  receiveAllPending(): void { if (!this.receiving() || this.saving() || this.orderConflict()) return; for (const line of this.receipt.lines) line.quantity = this.pendingFor(line.orderLineId); }
  pendingFor(orderLineId: string): number { return this.selectedOrder()?.lines.find(l => l.id === orderLineId)?.pendingQuantity ?? 0; }
  receiptValidation(): string { const order = this.selectedOrder(); if (!order || !this.receipt.invoiceDate) return 'Indica la fecha de factura.'; const lines = this.receipt.lines.filter(l => l.quantity > 0); if (!lines.length) return 'Indica al menos una cantidad a recibir.'; for (const line of this.receipt.lines) { const original = order.lines.find(l => l.id === line.orderLineId); if (!original || !Number.isInteger(line.quantity) || line.quantity < 0 || line.quantity > original.pendingQuantity) return 'La cantidad debe ser entera y no superar lo pendiente.'; if (line.quantity > 0 && !(Number(line.unitCost) > 0)) return `Escribe el costo de ${this.productName(line.productId)}: lo que cobró el proveedor por unidad.`; if (line.quantity > 0 && this.hasNoPrice(line.productId) && !(Number(line.newRetailPrice) > 0)) return `${this.productName(line.productId)} todavía no tiene precio de venta: escríbelo en «Precio nuevo».`; if (line.quantity > 0 && (!Number.isFinite(line.unitCost) || line.unitCost <= 0 || Math.round(line.unitCost * 10000) !== line.unitCost * 10000 || (this.receipt.vatMode === 'NO_DISCRIMINADO' && line.vatPercentage != null) || (line.vatPercentage != null && (line.vatPercentage < 0 || line.vatPercentage > 100 || Math.round(line.vatPercentage * 100) !== line.vatPercentage * 100)))) return 'Indica el costo de cada producto recibido.'; if (line.quantity > 0 && this.newPrice[line.orderLineId] && (line.newRetailPrice == null || `${line.newRetailPrice}` === '')) return `Escribe el precio nuevo de ${this.productName(line.productId)} o marca «Mismo precio».`; if (line.quantity > 0 && line.newRetailPrice != null && (!Number.isFinite(line.newRetailPrice) || line.newRetailPrice <= 0 || !Number.isInteger(line.newRetailPrice))) return 'El precio nuevo debe ser un valor entero mayor que cero.'; } return ''; }
  async receive(): Promise<void> {
    const order = this.selectedOrder(); if (!order || this.saving() || !this.canReceive || this.orderConflict()) return;
    const issue = this.receiptValidation(); if (issue) { this.error.set(issue); return; }
    const input = { supplierDocumentNumber: this.receipt.supplierDocumentNumber?.trim() || null, invoiceDate: this.receipt.invoiceDate, vatMode: this.receipt.vatMode,
      lines: this.receipt.lines.filter(l => l.quantity > 0).map(l => ({ ...l, newRetailPrice: this.newPrice[l.orderLineId] ? this.changedPrice(l.productId, l.newRetailPrice) : null })) };
    const priced = input.lines.filter(l => l.newRetailPrice != null);
    // D5 en la recepción (25/11/2026): el Supervisor pone el precio nuevo con la autorización del Administrador.
    if (priced.length && !this.canChangeRetailPrice) {
      await this.authorization.open({ title: 'Autorizar precio de venta nuevo',
        description: 'El Administrador autoriza el precio de venta nuevo (su carné, o su usuario y contraseña). La mercancía se recibe al autorizar.',
        detail: priced.map(l => `${this.productName(l.productId)}: ${groupedNumber(this.productRetailPrice(l.productId) ?? 0)} → ${groupedNumber(l.newRetailPrice!)}`).join(' · '),
        operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true,
        onError: message => this.error.set(message),
        run: credentials => this.submitReceipt(order, input, credentials) });
      return;
    }
    await this.submitReceipt(order, input, null).catch(() => undefined);
  }
  private async submitReceipt(order: Order, input: { supplierDocumentNumber: string | null; invoiceDate: string; vatMode: string; lines: ReceiptLine[] },
    credentials: AuthorizationCredentials | null): Promise<void> {
    const body = JSON.stringify({ orderId: order.id, version: order.version, input });
    if (this.pendingReceipt?.body !== body) this.pendingReceipt = { body, key: crypto.randomUUID() };
    this.saving.set(true); this.error.set('');
    try {
      const result = await this.ordersApi.receive(order, this.pendingReceipt.key, credentials ? { ...input, authorization: authorizationBody(credentials) } : input);
      this.pendingReceipt = null; this.receiving.set(false); this.selectedPurchase.set(result.purchase);
      this.selectedOrder.set(await this.ordersApi.get(order.id)); this.notice.set('Recepción registrada.'); await this.reload();
    } catch (e) {
      if (this.isConflict(e)) { this.orderConflict.set(true); this.pendingReceipt = null; }
      this.error.set(errorText(e)); throw e;
    } finally { this.saving.set(false); }
  }
  async startCancel(): Promise<void> { if (!this.canCancel) return; this.error.set(''); try { this.reasons.set(await this.api.reasons()); this.cancelling.set(true); } catch (e) { this.error.set(errorText(e)); } }
  async cancelPurchase(): Promise<void> { const p = this.selectedPurchase(); if (!p || !this.canCancel || this.saving() || !this.cancelReasonId) { this.error.set('Selecciona un motivo de anulación.'); return; } if (this.reasons().find(r => r.id === this.cancelReasonId)?.code === 'OTRO' && !this.cancelObservation.trim()) { this.error.set('Describe el motivo de la anulación.'); return; } await this.authorization.open({ title: 'Autorizar anulación de compra', description: 'Se anulará completamente la compra confirmada y se revertirá su inventario cuando el contrato lo permita.', detail: `Compra ${p.visibleNumber}`, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: false, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message), run: credentials => this.executeCancelPurchase(p, credentials) }); }
  private async executeCancelPurchase(p: Purchase, credentials: AuthorizationCredentials | null): Promise<void> { if (this.saving()) throw new Error('La anulación ya está en curso.'); const body = JSON.stringify({ id: p.id, version: p.version, reasonId: this.cancelReasonId, observation: this.cancelObservation.trim() || null }); if (this.pendingCancellation?.body !== body) this.pendingCancellation = { body, key: crypto.randomUUID() }; this.saving.set(true); this.error.set(''); try { this.selectedPurchase.set(credentials?.credentialToken ? await this.api.cancel(p, this.pendingCancellation.key, this.cancelReasonId, this.cancelObservation.trim() || null, null, null, credentials.credentialToken) : await this.api.cancel(p, this.pendingCancellation.key, this.cancelReasonId, this.cancelObservation.trim() || null, credentials?.userName ?? null, credentials?.password ?? null)); this.pendingCancellation = null; this.cancelling.set(false); this.notice.set('Compra anulada.'); await this.reload(); } catch (e) { if (this.isConflict(e)) this.pendingCancellation = null; this.error.set(errorText(e)); throw e; } finally { this.saving.set(false); } }
  async startReceiptCorrection(): Promise<void> {
    const purchase = this.selectedPurchase();
    if (!purchase || purchase.status !== 'CONFIRMADA' || !purchase.orderId || !this.canCorrectReceipt || this.saving()) return;
    this.loading.set(true); this.error.set('');
    try {
      const preparation = await this.api.correctionPreparation(purchase.id);
      this.correctionPreparation.set(preparation);
      this.reasons.set(preparation.canReverse ? await this.api.reasons() : []);
      this.correctionReasonId = ''; this.correctionObservation = ''; this.correctingReceipt.set(true);
    } catch (e) { this.error.set(errorText(e)); }
    finally { this.loading.set(false); }
  }
  closeReceiptCorrection(): void { if (this.saving()) return; this.correctingReceipt.set(false); this.correctionPreparation.set(null); }
  async submitReceiptCorrection(): Promise<void> {
    const purchase = this.selectedPurchase(); const preparation = this.correctionPreparation();
    if (!purchase || !preparation || !preparation.canReverse || this.saving()) return;
    if (!this.correctionReasonId) { this.error.set('Selecciona el motivo de la corrección.'); return; }
    if (this.correctionReasonIsOther && !this.correctionObservation.trim()) { this.error.set('Describe el motivo de la corrección.'); return; }
    if (this.correctionObservation.length > 500) { this.error.set('La observación supera 500 caracteres.'); return; }
    await this.authorization.open({ title: 'Autorizar corrección de recepción', description: `Se retirarán ${groupedNumber(preparation.unitsToRemove)} unidades de esta recepción y volverán a quedar pendientes en el pedido.${preparation.requiresAdministratorAuthorization ? ' Un Supervisor la hace con la autorización del Administrador.' : ''}`, detail: `Compra ${purchase.visibleNumber ?? 'confirmada'} · Total ${groupedNumber(preparation.originalTotal)}`, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: !!preparation.requiresAdministratorAuthorization, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message), run: credentials => this.executeReceiptCorrection(purchase, preparation, credentials) });
  }
  private async executeReceiptCorrection(purchase: Purchase, preparation: PurchaseCorrectionPreparation, credentials: AuthorizationCredentials | null): Promise<void> {
    if (this.saving()) throw new Error('La corrección ya está en curso.');
    const body = JSON.stringify({ id: purchase.id, purchaseVersion: preparation.purchaseVersion, orderVersion: preparation.orderVersion, reasonId: this.correctionReasonId, observation: this.correctionObservation.trim() || null });
    if (this.pendingCorrection?.body !== body) this.pendingCorrection = { body, key: crypto.randomUUID() };
    this.saving.set(true); this.error.set('');
    try {
      const result = credentials?.credentialToken
        ? await this.api.correctReceipt(purchase, preparation, this.pendingCorrection.key, this.correctionReasonId, this.correctionObservation.trim() || null, null, null, credentials.credentialToken)
        : await this.api.correctReceipt(purchase, preparation, this.pendingCorrection.key, this.correctionReasonId, this.correctionObservation.trim() || null, credentials?.userName ?? null, credentials?.password ?? null);
      this.selectedPurchase.set(result); this.pendingCorrection = null; this.correctingReceipt.set(false); this.correctionPreparation.set(null);
      this.notice.set('Recepción corregida. Las cantidades volvieron a quedar pendientes en el pedido.');
      await this.reload();
    } catch (e) { if (this.isConflict(e)) this.pendingCorrection = null; this.error.set(errorText(e)); throw e; }
    finally { this.saving.set(false); }
  }
}
