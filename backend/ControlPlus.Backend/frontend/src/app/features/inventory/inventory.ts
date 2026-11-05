import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { Adjustment, InventoryApi, Movement, Reason, Stock } from './inventory-api';
import { Icon } from '../../layout/icon';
import { AuthorizationCredentials, AuthorizationDialog } from '../../shared/authorization-dialog';
@Component({ selector: 'cp-inventory', imports: [FormsModule, MatPaginatorModule, DatePipe, Icon], templateUrl: './inventory.html', styleUrl: '../directory.scss' })
export class Inventory implements OnInit {
  private readonly api = inject(InventoryApi);
  private readonly authorization = inject(AuthorizationDialog);
  readonly session = inject(SessionService);
  readonly stocks = signal<Page<Stock> | null>(null);
  readonly movements = signal<Page<Movement> | null>(null);
  readonly reasons = signal<Reason[]>([]);
  readonly selected = signal<Stock | null>(null);
  readonly adjustmentOpen = signal(false);
  readonly adjustmentProductId = signal('');
  readonly loading = signal(false); readonly historyLoading = signal(false); readonly saving = signal(false);
  readonly error = signal(''); readonly notice = signal(''); readonly conflict = signal(false);
  readonly movementTypes = ['COMPRA', 'ANULACION_COMPRA', 'VENTA', 'ANULACION_VENTA', 'RESERVA_APARTADO', 'LIBERACION_APARTADO', 'ENTREGA_APARTADO', 'CAMBIO_DEVOLUCION', 'CAMBIO_ENTREGA', 'AJUSTE_ENTRADA', 'AJUSTE_SALIDA', 'REVERSO'];
  search = ''; includeInactive = false; page = 1; pageSize = 20; movementPage = 1; movementType = ''; from = ''; to = '';
  quantityDelta = 0; reasonId = ''; observation = '';
  private requestId = 0;
  private pending: { key: string; input: Omit<Adjustment, 'authorization'>; authorizerUserName: string | null } | null = null;
  get canAdjust(): boolean { return this.session.has('INVENTORY.ADJUST'); }
  ngOnInit(): void { void this.reload(); if (this.canAdjust) void this.loadReasons(); }
  async reload(): Promise<void> { const id = ++this.requestId; this.loading.set(true); this.error.set(''); try { const result = await this.api.stock({ search: this.search.trim(), includeInactive: this.session.has('PRODUCTS.SENSITIVE_UPDATE') && this.includeInactive, page: this.page, pageSize: this.pageSize }); if (id === this.requestId) this.stocks.set(result); } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); } finally { if (id === this.requestId) this.loading.set(false); } }
  find(): void { this.page = 1; void this.reload(); }
  stockPage(e: PageEvent): void { this.page = e.pageIndex + 1; this.pageSize = e.pageSize; void this.reload(); }
  async loadReasons(): Promise<void> { try { this.reasons.set(await this.api.reasons()); } catch (e) { this.error.set(errorText(e)); } }
  select(stock: Stock): void { if (this.pending && !confirm('Hay un ajuste con respuesta incierta. ¿Descartar su clave y cambiar de producto?')) return; this.selected.set(stock); this.movementPage = 1; this.pending = null; this.conflict.set(false); this.resetForm(); void this.loadMovements(); }
  openAdjustment(stock?: Stock): void { if (this.pending) return; if (stock) this.select(stock); else { this.selected.set(null); this.movements.set(null); } this.adjustmentProductId.set(stock?.productId ?? ''); this.adjustmentOpen.set(true); this.error.set(''); }
  closeAdjustment(): void { if (this.saving()) return; if (this.pending && !confirm('El ajuste tiene respuesta incierta. ¿Descartar la clave?')) return; this.pending = null; this.adjustmentOpen.set(false); this.conflict.set(false); this.error.set(''); }
  chooseAdjustmentProduct(id: string): void { const stock = this.stocks()?.items.find(x => x.productId === id); this.adjustmentProductId.set(id); if (stock) this.select(stock); else this.selected.set(null); }
  closeHistory(): void { this.selected.set(null); this.movements.set(null); }
  private resetForm(): void { this.quantityDelta = 0; this.reasonId = ''; this.observation = ''; }
  async loadMovements(): Promise<void> { const stock = this.selected(); if (!stock) return; this.historyLoading.set(true); this.error.set(''); try { this.movements.set(await this.api.movements({ productId: stock.productId, type: this.movementType || undefined, fromUtc: this.from ? new Date(this.from).toISOString() : undefined, toUtc: this.to ? new Date(this.to).toISOString() : undefined, page: this.movementPage, pageSize: 20 })); } catch (e) { this.error.set(errorText(e)); } finally { this.historyLoading.set(false); } }
  historyPage(e: PageEvent): void { this.movementPage = e.pageIndex + 1; void this.loadMovements(); }
  historyFilter(): void { this.movementPage = 1; void this.loadMovements(); }
  async refreshSelected(): Promise<void> { const id = this.selected()?.productId; this.pending = null; this.conflict.set(false); this.resetForm(); await this.reload(); const current = this.stocks()?.items.find(x => x.productId === id); if (current) this.select(current); else { this.selected.set(null); this.movements.set(null); this.notice.set('Busca el producto para ver su versión actual.'); } }
  async adjust(): Promise<void> {
    const stock = this.selected(); if (!stock || !this.canAdjust || this.saving() || this.conflict()) return;
    if (!this.pending) {
      if (!stock.isActive || !Number.isSafeInteger(Number(this.quantityDelta)) || Number(this.quantityDelta) === 0 || stock.currentStock + Number(this.quantityDelta) < stock.reservedStock || !this.reasonId) { this.error.set('Revisa cantidad y motivo del ajuste.'); return; }
    }
    await this.authorization.open({ title: 'Autorizar ajuste de inventario',
      description: 'Se modificará la existencia y se registrará un movimiento auditado.',
      detail: `${stock.name} · ${this.quantityDelta > 0 ? '+' : ''}${this.quantityDelta} unidades`,
      operatorUserName: this.session.user()?.userName ?? '', requireDifferentUser: true, selfAuthorizedRoles: ['ADMINISTRADOR'],
      onError: message => this.error.set(message),
      run: credentials => this.submitAdjustment(credentials) });
  }
  private async submitAdjustment(credentials: AuthorizationCredentials | null): Promise<void> {
    const stock = this.selected(); if (!stock || this.saving() || this.conflict()) throw new Error('Actualiza existencias antes de reintentar.');
    if (this.pending && this.pending.authorizerUserName !== (credentials?.userName.toUpperCase() ?? null))
      throw new Error('Reintenta con el mismo usuario autorizador; la clave del ajuste conserva su identidad.');
    if (!this.pending) this.pending = { key: crypto.randomUUID(), authorizerUserName: credentials?.userName.toUpperCase() ?? null, input: { productId: stock.productId, quantityDelta: Number(this.quantityDelta), reasonId: this.reasonId, expectedVersion: stock.version, observation: this.observation.trim() || null } };
    this.saving.set(true); this.error.set(''); this.notice.set('');
    try { await this.api.adjust(credentials ? { ...this.pending.input, authorization: credentials } : { ...this.pending.input }, this.pending.key); this.pending = null; this.resetForm(); this.notice.set('Ajuste registrado.'); this.adjustmentOpen.set(false); await this.refreshSelected(); }
    catch (e) { if (e instanceof HttpErrorResponse && e.status === 409) { this.conflict.set(true); this.pending = null; this.error.set('Conflicto de versión o clave. Actualiza existencias y revisa el ajuste antes de crear otro intento.'); } else if (e instanceof HttpErrorResponse && e.status >= 400 && e.status < 500) { this.pending = null; this.error.set(errorText(e)); } throw e; }
    finally { this.saving.set(false); }
  }
  discardPending(): void { if (this.saving()) return; this.pending = null; this.error.set(''); }
  get hasPending(): boolean { return this.pending !== null; }
}
