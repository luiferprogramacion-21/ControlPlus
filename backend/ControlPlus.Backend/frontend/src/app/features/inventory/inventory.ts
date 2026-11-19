import { groupedNumber, NumberInput } from '../../shared/number-input';
import { A11yModule } from '@angular/cdk/a11y';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Adjustment, InventoryApi, Movement, Reason, Stock, StockStatusFilter } from './inventory-api';
import { Icon } from '../../layout/icon';
import { AuthorizationCredentials, AuthorizationDialog, authorizationBody, authorizationIdentity } from '../../shared/authorization-dialog';
import { toastSignal } from '../../core/toast';
import { ConfirmationDialog } from '../../shared/confirmation-dialog';
import { WindowSheet } from '../../shared/window-sheet';
@Component({ selector: 'cp-inventory', imports: [WindowSheet, NumberInput, DecimalPipe, A11yModule, FormsModule, MatPaginatorModule, DatePipe, Icon], templateUrl: './inventory.html', styleUrls: ['../directory.scss', './inventory.scss'] })
export class Inventory implements OnInit {
  private readonly api = inject(InventoryApi);
  private readonly authorization = inject(AuthorizationDialog);
  private readonly confirmation = inject(ConfirmationDialog);
  private readonly owner = inject(DestroyRef);
  readonly session = inject(SessionService);
  readonly stocks = signal<Page<Stock> | null>(null);
  readonly movements = signal<Page<Movement> | null>(null);
  readonly reasons = signal<Reason[]>([]);
  readonly selected = signal<Stock | null>(null);
  readonly historyOpen = signal(false);
  readonly adjustmentOpen = signal(false);
  readonly adjustmentProductId = signal('');
  readonly loading = signal(false); readonly historyLoading = signal(false); readonly saving = signal(false);
  readonly error = toastSignal('error'); readonly notice = toastSignal('success'); readonly conflict = signal(false);
  readonly movementTypes = ['COMPRA', 'ANULACION_COMPRA', 'VENTA', 'ANULACION_VENTA', 'RESERVA_APARTADO', 'LIBERACION_APARTADO', 'ENTREGA_APARTADO', 'CAMBIO_DEVOLUCION', 'CAMBIO_ENTREGA', 'AJUSTE_ENTRADA', 'AJUSTE_SALIDA', 'REVERSO'];
  search = ''; status: StockStatusFilter = ''; page = 1; pageSize = 20; movementPage = 1; movementType = ''; from = ''; to = '';
  quantityDelta = 0; reasonId = ''; observation = '';
  private requestId = 0;
  private movementRequestId = 0;
  private pending: { key: string; input: Omit<Adjustment, 'authorization'>; authorizerUserName: string | null } | null = null;
  get canAdjust(): boolean { return this.session.has('INVENTORY.ADJUST'); }
  get canSeeInactive(): boolean { return this.session.has('PRODUCTS.SENSITIVE_UPDATE'); }
  /** El Dashboard enlaza con ?filtro=stock-bajo para abrir directamente los productos que cuenta como alerta. */
  private readonly route = inject(ActivatedRoute, { optional: true });
  ngOnInit(): void {
    if (this.route?.snapshot.queryParamMap.get('filtro') === 'stock-bajo') this.status = 'STOCK_BAJO';
    void this.reload(); if (this.canAdjust) void this.loadReasons();
  }
  /** Mismo orden que el filtro: inactivo, agotado, stock bajo (disponible igual o menor al mínimo) y disponible. */
  stockStatus(stock: Stock): { label: string; tone: 'success' | 'warning' | 'error' | 'neutral' } {
    if (!stock.isActive) return { label: 'Inactivo', tone: 'neutral' };
    if (stock.availableStock <= 0) return { label: 'Agotado', tone: 'error' };
    if (stock.availableStock <= stock.minimumStock) return { label: 'Stock bajo', tone: 'warning' };
    return { label: 'Disponible', tone: 'success' };
  }
  async reload(): Promise<void> { const id = ++this.requestId; this.loading.set(true); this.error.set(''); try { const result = await this.api.stock({ search: this.search.trim(), status: this.status === 'INACTIVO' && !this.canSeeInactive ? '' : this.status, page: this.page, pageSize: this.pageSize }); if (id === this.requestId) this.stocks.set(result); } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); } finally { if (id === this.requestId) this.loading.set(false); } }
  find(): void { this.page = 1; void this.reload(); }
  stockPage(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  async loadReasons(): Promise<void> { try { this.reasons.set(await this.api.reasons()); } catch (e) { this.error.set(errorText(e)); } }
  async select(stock: Stock): Promise<boolean> { if (!await this.selectStock(stock) || this.selected()?.productId !== stock.productId) return false; void this.loadMovements(); return true; }
  private async selectStock(stock: Stock | null): Promise<boolean> {
    if (this.saving()) return false;
    const pending = this.pending; const selected = this.selected();
    if (pending && !await this.confirmation.confirm({ title: 'Cambiar de producto', message: 'Hay un ajuste con respuesta incierta. ¿Descartar su clave y cambiar de producto?', confirmText: 'Descartar y cambiar', danger: true }, this.owner)) return false;
    if (this.owner.destroyed || this.saving() || this.pending !== pending || this.selected() !== selected) return false;
    this.applyStockSelection(stock); return true;
  }
  private applyStockSelection(stock: Stock | null): void { this.selected.set(stock); this.movements.set(null); this.historyLoading.set(false); ++this.movementRequestId; this.movementPage = 1; this.pending = null; this.conflict.set(false); this.resetForm(); }
  async openHistory(stock: Stock): Promise<void> { if (await this.select(stock) && this.selected()?.productId === stock.productId) this.historyOpen.set(true); }
  async openAdjustment(stock?: Stock): Promise<void> { if (this.pending || this.saving() || !await this.selectStock(stock ?? null) || this.saving() || this.selected() !== (stock ?? null)) return; this.adjustmentProductId.set(stock?.productId ?? ''); this.adjustmentOpen.set(true); this.error.set(''); }
  async closeAdjustment(): Promise<void> {
    if (this.saving()) return;
    const pending = this.pending; const selected = this.selected();
    if (pending && !await this.confirmation.confirm({ title: 'Cerrar ajuste pendiente', message: 'El ajuste tiene respuesta incierta. ¿Descartar la clave?', confirmText: 'Descartar y cerrar', danger: true }, this.owner)) return;
    if (this.owner.destroyed || this.saving() || this.pending !== pending || this.selected() !== selected) return;
    this.pending = null; this.adjustmentOpen.set(false); this.conflict.set(false); this.error.set('');
  }
  async chooseAdjustmentProduct(id: string): Promise<void> { const stock = this.stocks()?.items.find(x => x.productId === id) ?? null; if (await this.selectStock(stock) && this.selected() === stock) this.adjustmentProductId.set(stock?.productId ?? ''); }
  closeHistory(): void { this.historyOpen.set(false); this.selected.set(null); this.movements.set(null); this.historyLoading.set(false); ++this.movementRequestId; this.error.set(''); }
  private resetForm(): void { this.quantityDelta = 0; this.reasonId = ''; this.observation = ''; }
  async loadMovements(): Promise<void> { const stock = this.selected(); if (!stock) return; const id = ++this.movementRequestId; this.historyLoading.set(true); this.error.set(''); try { const result = await this.api.movements({ productId: stock.productId, type: this.movementType || undefined, fromUtc: this.from ? new Date(this.from).toISOString() : undefined, toUtc: this.to ? new Date(this.to).toISOString() : undefined, page: this.movementPage, pageSize: 20 }); if (id === this.movementRequestId && this.selected()?.productId === stock.productId) this.movements.set(result); } catch (e) { if (id === this.movementRequestId) this.error.set(errorText(e)); } finally { if (id === this.movementRequestId) this.historyLoading.set(false); } }
  historyPage(e: PageEvent): void { this.movementPage = e.pageIndex + 1; void this.loadMovements(); }
  historyFilter(): void { this.movementPage = 1; void this.loadMovements(); }
  async refreshSelected(): Promise<void> { const id = this.selected()?.productId; this.pending = null; this.conflict.set(false); this.resetForm(); await this.reload(); if (this.selected()?.productId !== id || this.owner.destroyed) return; const current = this.stocks()?.items.find(x => x.productId === id); if (current) this.applyStockSelection(current); else { this.applyStockSelection(null); this.notice.set('Busca el producto para ver su versión actual.'); } }
  async adjust(): Promise<void> {
    const stock = this.selected(); if (!stock || !this.canAdjust || this.saving() || this.conflict()) return;
    if (!this.pending) {
      if (!stock.isActive || !Number.isSafeInteger(Number(this.quantityDelta)) || Number(this.quantityDelta) === 0 || stock.currentStock + Number(this.quantityDelta) < stock.reservedStock || !this.reasonId) { this.error.set('Revisa cantidad y motivo del ajuste.'); return; }
    }
    await this.authorization.open({ title: 'Autorizar ajuste de inventario',
      description: 'Se modificará la existencia y se registrará un movimiento auditado.',
      detail: `${stock.name} · ${this.quantityDelta > 0 ? '+' : ''}${groupedNumber(this.quantityDelta)} unidades`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, allowCredential: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
      onError: message => this.error.set(message),
      run: credentials => this.submitAdjustment(credentials) });
  }
  private async submitAdjustment(credentials: AuthorizationCredentials | null): Promise<void> {
    const stock = this.selected(); if (!stock || this.saving() || this.conflict()) throw new Error('Actualiza existencias antes de reintentar.');
    if (this.pending && this.pending.authorizerUserName !== authorizationIdentity(credentials))
      throw new Error('Reintenta con el mismo usuario autorizador; la clave del ajuste conserva su identidad.');
    if (!this.pending) this.pending = { key: crypto.randomUUID(), authorizerUserName: authorizationIdentity(credentials), input: { productId: stock.productId, quantityDelta: Number(this.quantityDelta), reasonId: this.reasonId, expectedVersion: stock.version, observation: this.observation.trim() || null } };
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try { await this.api.adjust(credentials ? { ...this.pending.input, authorization: authorizationBody(credentials) } : { ...this.pending.input }, this.pending.key); this.pending = null; this.resetForm(); this.notice.set('Ajuste registrado.'); this.adjustmentOpen.set(false); await this.refreshSelected(); }
    catch (e) { if (e instanceof HttpErrorResponse && e.status === 409) { this.conflict.set(true); this.pending = null; this.error.set('Conflicto de versión o clave. Actualiza existencias y revisa el ajuste antes de crear otro intento.'); } else if (e instanceof HttpErrorResponse && e.status >= 400 && e.status < 500) { this.pending = null; this.error.set(errorText(e)); } throw e; }
    finally { this.saving.set(false); }
  }
  discardPending(): void { if (this.saving()) return; this.pending = null; this.error.set(''); }
  get hasPending(): boolean { return this.pending !== null; }
}
