import { groupedNumber, NumberInput } from '../../shared/number-input';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Icon } from '../../layout/icon';
import { CatalogApi, Product } from '../catalog/catalog-api';
import { Supplier, SuppliersApi } from '../suppliers/suppliers-api';
import { Order, OrderReason, OrderSummary, OrdersApi, ReceiptLine } from './orders-api';
import { CancellationReason, Purchase, PurchaseInput, PurchaseLineInput, PurchaseSummary, PurchasesApi } from './purchases-api';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody } from '../../shared/authorization-dialog';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PurchaseOrderDocumentPrinter } from './purchase-order-document';
import { profitPercentOf, suggestedPrice } from '../../shared/price-calculator';
import { toastSignal } from '../../core/toast';
import { WindowSheet } from '../../shared/window-sheet';

export function currentColombiaDate(now = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Bogota', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(now);
  const value = Object.fromEntries(parts.map(part => [part.type, part.value]));
  return `${value['year']}-${value['month']}-${value['day']}`;
}
@Component({ selector: 'cp-purchases', imports: [WindowSheet, NumberInput, CommonModule, FormsModule, MatPaginatorModule, Icon, RouterLink], templateUrl: './purchases.html', styleUrls: ['../directory.scss', './purchases.scss'] })
export class Purchases implements OnInit {
  private readonly api = inject(PurchasesApi); private readonly ordersApi = inject(OrdersApi); private readonly suppliersApi = inject(SuppliersApi); private readonly catalog = inject(CatalogApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly orderDocument = inject(PurchaseOrderDocumentPrinter);
  private readonly route = inject(ActivatedRoute, { optional: true });
  readonly session = inject(SessionService); readonly purchases = signal<Page<PurchaseSummary> | null>(null); readonly orders = signal<Page<OrderSummary> | null>(null); readonly selectedPurchase = signal<Purchase | null>(null); readonly selectedOrder = signal<Order | null>(null); readonly suppliers = signal<Supplier[]>([]); readonly products = signal<Product[]>([]); readonly reasons = signal<CancellationReason[]>([]);
  readonly loading = signal(false); readonly saving = signal(false); readonly error = toastSignal('error'); readonly notice = toastSignal('success'); readonly editing = signal(false); readonly receiving = signal(false); readonly cancelling = signal(false);
  readonly choosingReceipt = signal(false);
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
  cancelReasonId = ''; cancelObservation = '';
  private requestId = 0;
  private pendingCancellation: { body: string; key: string } | null = null;
  private pendingReceipt: { body: string; key: string } | null = null;
  get canOrders(): boolean { return this.session.has('PURCHASE_ORDERS.MANAGE'); }
  get canWriteOrders(): boolean { return this.canOrders && this.session.has('SUPPLIERS.READ') && this.session.has('PRODUCTS.READ') && this.session.has('INVENTORY.READ') && this.session.hasRole('SUPERVISOR', 'ADMINISTRADOR'); }
  get canReceive(): boolean { return this.session.has('PURCHASES.RECEIVE'); }
  get canChangeRetailPrice(): boolean { return this.session.hasRole('ADMINISTRADOR') && this.session.has('PRODUCTS.SENSITIVE_UPDATE'); }
  get canCancelOrder(): boolean { return this.canOrders && this.session.has('PURCHASE_ORDERS.CANCEL') && this.session.hasRole('ADMINISTRADOR'); }
  get canCloseOrder(): boolean { return this.canOrders && this.session.hasRole('SUPERVISOR', 'ADMINISTRADOR'); }
  get orderPendingTotal(): number { return this.selectedOrder()?.lines.reduce((sum, line) => sum + line.pendingQuantity, 0) ?? 0; }
  get orderReceivedTotal(): number { return this.selectedOrder()?.lines.reduce((sum, line) => sum + line.receivedQuantity, 0) ?? 0; }
  get canStartOrderCancel(): boolean { const order = this.selectedOrder(); return this.canCancelOrder && !!order && (order.status === 'BORRADOR' || order.status === 'ENVIADO') && this.orderReceivedTotal === 0; }
  get canStartOrderClose(): boolean { const order = this.selectedOrder(); return this.canCloseOrder && !!order && (order.status === 'ENVIADO' || order.status === 'RECIBIDO_PARCIAL') && this.orderPendingTotal > 0; }
  get orderReasonIsOther(): boolean { return this.orderReasons().find(reason => reason.id === this.orderReasonId)?.code === 'OTRO'; }
  get canCancel(): boolean { return this.session.has('PURCHASES.CANCEL') && this.session.hasRole('ADMINISTRADOR'); }
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
  receivedTotal(order: OrderSummary): number { return order.lines?.reduce((sum, line) => sum + line.receivedQuantity, 0) ?? 0; }
  pendingTotal(order: OrderSummary): number { return this.requestedTotal(order) - this.receivedTotal(order); }
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
  async submitOrderTransition(): Promise<void> { const kind = this.orderTransition(); const order = this.selectedOrder(); if (!kind || !order || this.saving() || this.orderConflict()) return; const issue = this.orderTransitionValidation(); if (issue) { this.error.set(issue); return; } await this.authorization.open({ title: kind === 'close' ? 'Autorizar cierre de pedido' : 'Autorizar anulación de pedido', description: kind === 'close' ? `Quedarán ${groupedNumber(this.orderPendingTotal)} unidades pendientes sin recibir; no se registrará mercancía.` : 'El pedido quedará anulado en el historial sin revertir inventario.', detail: `Pedido ${order.visibleNumber} · versión ${order.version}`, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: false, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message), run: credentials => this.executeOrderTransition(kind, order, credentials) }); }
  private async executeOrderTransition(kind: 'close' | 'cancel', order: Order, credentials: AuthorizationCredentials | null): Promise<void> { if (this.saving() || this.orderConflict()) throw new Error('Actualiza el pedido antes de reintentar.'); this.saving.set(true); this.error.set(''); try { const input = { reasonId: this.orderReasonId, observation: this.orderObservation.trim() || null, ...(credentials ? { authorization: authorizationBody(credentials) } : {}) }; const result = kind === 'close' ? await this.ordersApi.close(order, input) : await this.ordersApi.cancel(order, input); this.selectedOrder.set(result); this.orderTransition.set(null); this.notice.set(kind === 'close' ? 'Pedido cerrado con pendientes. No se registró recepción.' : 'Pedido anulado. Se conserva su historial.'); await this.reload(); } catch (e) { if (this.isConflict(e)) this.orderConflict.set(true); this.error.set(errorText(e)); throw e; } finally { this.saving.set(false); } }
  private isConflict(error: unknown): boolean { return !!error && typeof error === 'object' && 'status' in error && error.status === 409; }
  async newOrder(): Promise<void> { if (!this.canWriteOrders) return; this.orderDraft = null; this.orderSupplierId = ''; this.orderLines = []; this.error.set(''); this.editingOrder.set(true); await this.loadProducts(); }
  async editOrder(order: Order): Promise<void> { if (!this.canWriteOrders || order.status !== 'BORRADOR') return; this.orderDraft = order; this.orderSupplierId = order.supplierId; this.error.set(''); this.editingOrder.set(true); await this.loadProducts(); this.setOrderSupplier(order.supplierId); for (const line of order.lines) { const item = this.orderLines.find(x => x.productId === line.productId); if (item) { item.selected = true; item.quantity = line.requestedQuantity; } } }
  setOrderSupplier(id: string): void { this.orderSupplierId = id; this.orderLines = this.products().filter(p => p.isActive && p.supplierId === id).map(p => ({ productId: p.id, productName: p.name, productCode: p.internalCode, available: p.availableStock, selected: false, quantity: 1 })); }
  orderValidation(): string { if (!this.orderSupplierId || !this.suppliers().some(s => s.id === this.orderSupplierId && s.isActive)) return 'Selecciona un proveedor activo.'; const lines = this.orderLines.filter(l => l.selected); if (!lines.length || lines.length > 200) return 'Incluye entre 1 y 200 productos.'; if (lines.some(l => !Number.isInteger(l.quantity) || l.quantity <= 0)) return 'Indica cantidades enteras positivas.'; return ''; }
  async saveOrder(): Promise<void> { if (!this.canWriteOrders || this.saving()) return; const issue = this.orderValidation(); if (issue) { this.error.set(issue); return; } this.saving.set(true); this.error.set(''); try { const lines = this.orderLines.filter(l => l.selected).map(l => ({ productId: l.productId, requestedQuantity: l.quantity })); const result = this.orderDraft ? await this.ordersApi.update(this.orderDraft, lines) : await this.ordersApi.create({ supplierId: this.orderSupplierId, lines }); this.selectedOrder.set(result); this.editingOrder.set(false); this.notice.set('Pedido guardado.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async sendOrder(): Promise<void> { const order = this.selectedOrder(); if (!order || order.status !== 'BORRADOR' || !this.canWriteOrders || this.saving()) return; this.saving.set(true); this.error.set(''); try { const sent = await this.ordersApi.send(order); this.selectedOrder.set(sent); this.notice.set('Pedido enviado. Generando documento…'); await this.reload(); await this.printOrder(sent, true); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async printOrder(order: Order | null = this.selectedOrder(), generatedAfterSend = false): Promise<void> { if (!order || this.printingOrder()) return; this.printingOrder.set(true); this.error.set(''); try { const supplier = this.suppliers().find(item => item.id === order.supplierId) ?? await this.suppliersApi.get(order.supplierId); await this.orderDocument.print(order, supplier, this.session.user()?.displayName ?? ''); this.notice.set(generatedAfterSend ? 'Pedido enviado y documento generado.' : 'Documento del pedido listo para imprimir o guardar como PDF.'); } catch (e) { this.error.set(generatedAfterSend ? `El pedido fue enviado, pero no se pudo abrir el documento. ${errorText(e)}` : errorText(e)); } finally { this.printingOrder.set(false); } }
  async newPurchase(): Promise<void> { if (!this.canReceive) return; this.selectedPurchase.set(null); this.form = this.empty(); this.editing.set(true); await this.loadProducts(); }
  async editPurchase(p: Purchase): Promise<void> { if (!this.canReceive || p.status !== 'BORRADOR') return; this.form = { supplierId: p.supplierId, supplierDocumentNumber: p.supplierDocumentNumber, invoiceDate: p.invoiceDate, vatMode: 'NO_DISCRIMINADO', lines: p.lines.map(l => ({ productId: l.productId, quantity: l.quantity, unitCost: l.unitCost, vatPercentage: null, newRetailPrice: l.newRetailPrice })) }; this.editing.set(true); await this.loadProducts(); }
  private async loadProducts(): Promise<void> { try { const all: Product[] = []; let page = 1, total = 1; do { const result = await this.catalog.products({ includeOutOfStock: true, page, pageSize: 100 }); all.push(...result.items); total = result.totalPages; page++; } while (page <= total); this.products.set(all); } catch (e) { this.error.set(errorText(e)); } }
  addLine(): void { this.form.lines.push({ productId: '', quantity: 1, unitCost: 0, vatPercentage: null, newRetailPrice: null }); }
  /** El precio nuevo es opcional: en blanco, el precio de venta del producto no cambia. */
  selectPurchaseProduct(line: PurchaseLineInput, productId: string): void { line.productId = productId; line.newRetailPrice = null; }
  /** Porcentaje de ganancia escrito en la calculadora, por producto (no viaja al servidor). */
  profit: Record<string, number | null> = {};
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
  purchaseValidation(): string { const f = this.form; if (!f.supplierId || !this.suppliers().some(s => s.id === f.supplierId && s.isActive)) return 'Selecciona un proveedor activo.'; if (!f.invoiceDate) return 'Indica la fecha de factura.'; if ((f.supplierDocumentNumber?.trim().length ?? 0) > 100) return 'El número de factura supera 100 caracteres.'; if (!f.lines.length || f.lines.length > 200) return 'Incluye entre 1 y 200 productos.'; if (new Set(f.lines.map(l => l.productId)).size !== f.lines.length) return 'Cada producto debe aparecer una sola vez.'; for (const l of f.lines) { if (!l.productId || !Number.isInteger(l.quantity) || l.quantity <= 0) return 'Indica producto y cantidad entera positiva.'; if (!Number.isFinite(l.unitCost) || l.unitCost <= 0 || Math.round(l.unitCost * 10000) !== l.unitCost * 10000) return 'Indica un costo positivo de hasta cuatro decimales.'; if (l.newRetailPrice != null && (!Number.isFinite(l.newRetailPrice) || l.newRetailPrice < 0 || !Number.isInteger(l.newRetailPrice))) return 'El precio nuevo debe ser un valor entero mayor o igual a cero.'; if (l.vatPercentage != null && (!Number.isFinite(l.vatPercentage) || l.vatPercentage < 0 || l.vatPercentage > 100 || Math.round(l.vatPercentage * 100) !== l.vatPercentage * 100)) return 'Revisa el IVA de las líneas.'; if (f.vatMode === 'NO_DISCRIMINADO' && l.vatPercentage != null) return 'Este modo de IVA no admite porcentaje por producto.'; } return ''; }
  async savePurchase(): Promise<void> { if (this.saving() || !this.canReceive) return; const issue = this.purchaseValidation(); if (issue) { this.error.set(issue); return; } this.saving.set(true); this.error.set(''); try { const selected = this.selectedPurchase(); const input = { ...this.form, supplierDocumentNumber: this.form.supplierDocumentNumber?.trim() || null, lines: this.form.lines.map(l => ({ ...l, vatPercentage: this.form.vatMode === 'NO_DISCRIMINADO' ? null : l.vatPercentage })) }; const result = selected ? await this.api.update(selected, input) : await this.api.create(input); this.selectedPurchase.set(result); this.editing.set(false); this.notice.set('Borrador guardado.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async confirmPurchase(): Promise<void> { const p = this.selectedPurchase(); if (!p || p.status !== 'BORRADOR' || this.saving() || !this.canReceive) return; this.saving.set(true); this.error.set(''); const key = crypto.randomUUID(); try { this.selectedPurchase.set(await this.api.confirm(p, key)); this.notice.set('Compra confirmada.'); await this.reload(); } catch (e) { this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  startReceipt(): void { const order = this.selectedOrder(); if (!order || !this.canReceive || this.orderConflict() || (order.status !== 'ENVIADO' && order.status !== 'RECIBIDO_PARCIAL') || !order.lines.some(l => l.pendingQuantity > 0)) return; this.receipt = { supplierDocumentNumber: null, invoiceDate: currentColombiaDate(), vatMode: 'NO_DISCRIMINADO', lines: order.lines.filter(l => l.pendingQuantity > 0).map(l => ({ orderLineId: l.id, productId: l.productId, quantity: 0, unitCost: l.estimatedUnitCost ?? 0, vatPercentage: null, newRetailPrice: null })) }; this.profit = {}; this.receiving.set(true); }
  receiveAllPending(): void { if (!this.receiving() || this.saving() || this.orderConflict()) return; for (const line of this.receipt.lines) line.quantity = this.pendingFor(line.orderLineId); }
  pendingFor(orderLineId: string): number { return this.selectedOrder()?.lines.find(l => l.id === orderLineId)?.pendingQuantity ?? 0; }
  receiptValidation(): string { const order = this.selectedOrder(); if (!order || !this.receipt.invoiceDate) return 'Indica la fecha de factura.'; const lines = this.receipt.lines.filter(l => l.quantity > 0); if (!lines.length) return 'Indica al menos una cantidad a recibir.'; for (const line of this.receipt.lines) { const original = order.lines.find(l => l.id === line.orderLineId); if (!original || !Number.isInteger(line.quantity) || line.quantity < 0 || line.quantity > original.pendingQuantity) return 'La cantidad debe ser entera y no superar lo pendiente.'; if (line.quantity > 0 && (!Number.isFinite(line.unitCost) || line.unitCost <= 0 || Math.round(line.unitCost * 10000) !== line.unitCost * 10000 || (this.receipt.vatMode === 'NO_DISCRIMINADO' && line.vatPercentage != null) || (line.vatPercentage != null && (line.vatPercentage < 0 || line.vatPercentage > 100 || Math.round(line.vatPercentage * 100) !== line.vatPercentage * 100)))) return 'Indica el costo de cada producto recibido.'; if (line.quantity > 0 && line.newRetailPrice != null && (!Number.isFinite(line.newRetailPrice) || line.newRetailPrice < 0 || !Number.isInteger(line.newRetailPrice))) return 'El precio nuevo debe ser un valor entero mayor o igual a cero.'; } return ''; }
  async receive(): Promise<void> { const order = this.selectedOrder(); if (!order || this.saving() || !this.canReceive || this.orderConflict()) return; const issue = this.receiptValidation(); if (issue) { this.error.set(issue); return; } const input = { supplierDocumentNumber: this.receipt.supplierDocumentNumber?.trim() || null, invoiceDate: this.receipt.invoiceDate, vatMode: this.receipt.vatMode, lines: this.receipt.lines.filter(l => l.quantity > 0).map(l => ({ ...l })) }; const body = JSON.stringify({ orderId: order.id, version: order.version, input }); if (this.pendingReceipt?.body !== body) this.pendingReceipt = { body, key: crypto.randomUUID() }; this.saving.set(true); this.error.set(''); try { const result = await this.ordersApi.receive(order, this.pendingReceipt.key, input); this.pendingReceipt = null; this.receiving.set(false); this.selectedPurchase.set(result.purchase); this.selectedOrder.set(await this.ordersApi.get(order.id)); this.notice.set('Recepción registrada.'); await this.reload(); } catch (e) { if (this.isConflict(e)) { this.orderConflict.set(true); this.pendingReceipt = null; } this.error.set(errorText(e)); } finally { this.saving.set(false); } }
  async startCancel(): Promise<void> { if (!this.canCancel) return; this.error.set(''); try { this.reasons.set(await this.api.reasons()); this.cancelling.set(true); } catch (e) { this.error.set(errorText(e)); } }
  async cancelPurchase(): Promise<void> { const p = this.selectedPurchase(); if (!p || !this.canCancel || this.saving() || !this.cancelReasonId) { this.error.set('Selecciona un motivo de anulación.'); return; } if (this.reasons().find(r => r.id === this.cancelReasonId)?.code === 'OTRO' && !this.cancelObservation.trim()) { this.error.set('Describe el motivo de la anulación.'); return; } await this.authorization.open({ title: 'Autorizar anulación de compra', description: 'Se anulará completamente la compra confirmada y se revertirá su inventario cuando el contrato lo permita.', detail: `Compra ${p.visibleNumber} · versión ${p.version}`, operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: false, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'], onError: message => this.error.set(message), run: credentials => this.executeCancelPurchase(p, credentials) }); }
  private async executeCancelPurchase(p: Purchase, credentials: AuthorizationCredentials | null): Promise<void> { if (this.saving()) throw new Error('La anulación ya está en curso.'); const body = JSON.stringify({ id: p.id, version: p.version, reasonId: this.cancelReasonId, observation: this.cancelObservation.trim() || null }); if (this.pendingCancellation?.body !== body) this.pendingCancellation = { body, key: crypto.randomUUID() }; this.saving.set(true); this.error.set(''); try { this.selectedPurchase.set(credentials?.credentialToken ? await this.api.cancel(p, this.pendingCancellation.key, this.cancelReasonId, this.cancelObservation.trim() || null, null, null, credentials.credentialToken) : await this.api.cancel(p, this.pendingCancellation.key, this.cancelReasonId, this.cancelObservation.trim() || null, credentials?.userName ?? null, credentials?.password ?? null)); this.pendingCancellation = null; this.cancelling.set(false); this.notice.set('Compra anulada.'); await this.reload(); } catch (e) { if (this.isConflict(e)) this.pendingCancellation = null; this.error.set(errorText(e)); throw e; } finally { this.saving.set(false); } }
}
