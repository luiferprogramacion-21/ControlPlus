import { DatePipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, computed, inject, Injectable, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { firstValueFrom } from 'rxjs';
import { Page, errorText } from '../../core/api';
import { SessionService } from '../../core/session.service';
import { AdminApi, AdminUser } from './admin-api';
import { AUDIT_ACTION_GROUPS, auditActionLabel, auditActionModule } from './audit-actions';
import { toastSignal } from '../../core/toast';
import { groupedNumber } from '../../shared/number-input';
import { readableCode } from '../../core/readable-code';
import { foldText, SuggestField, SuggestOption, suggestMatches } from '../../shared/suggest-field';

/** Sobre qué se hizo la acción, en palabras de la tienda (pruebas del usuario del 24/11/2026, punto 15). */
const ENTITIES: Record<string, string> = {
  User: 'Usuario', Usuario: 'Usuario', Role: 'Rol', Permission: 'Permiso', CredencialUsuario: 'Carné',
  Categoria: 'Categoría', Producto: 'Producto', Caja: 'Caja', TurnoCaja: 'Turno de caja', DetalleArqueoMedioPago: 'Conteo de la caja',
  MovimientoCaja: 'Movimiento de caja', AperturaCajon: 'Cajón de dinero', SesionOperador: 'Sesión de operador',
  Venta: 'Venta', CambioVenta: 'Cambio de venta', Cliente: 'Cliente', Credito: 'Crédito', PagoCredito: 'Abono a crédito',
  Apartado: 'Apartado', PagoApartado: 'Abono a apartado', MovimientoInventario: 'Inventario', Proveedor: 'Proveedor',
  Compra: 'Compra', PedidoCompra: 'Pedido al proveedor', TrabajoImpresion: 'Impresión', PerfilImpresora: 'Impresora',
  ConsecutivoDocumento: 'Numeración', MotivoOperacion: 'Motivos', Establecimiento: 'Establecimiento', DestinoRespaldo: 'Respaldo',
};

/** Internal identifiers mean nothing to the reader of the log (20 November 2026): they are left out of the detail. */
const HIDDEN_FIELD = /(^id$|Id$|Ids$|^ids$|_id$|correlation|version|Version|stamp|Stamp|hash|Hash|key$|Key$)/;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const WORDS: Record<string, string> = {
  total: 'total', paid: 'pagado', quantity: 'cantidad', amount: 'monto', reason: 'motivo', before: 'antes', after: 'después',
  lines: 'productos', line: 'producto', product: 'producto', products: 'productos', price: 'precio', cost: 'costo', unit: 'unitario',
  stock: 'existencias', name: 'nombre', code: 'código', document: 'documento', number: 'número', phone: 'teléfono', date: 'fecha',
  on: 'fecha', at: 'hora', note: 'nota', user: 'usuario', customer: 'cliente', sale: 'venta', payment: 'pago', payments: 'pagos',
  method: 'medio', change: 'cambio', received: 'recibido', balance: 'saldo', status: 'estado', state: 'estado', type: 'tipo',
  observation: 'observación', discount: 'descuento', initial: 'inicial', final: 'final', previous: 'anterior', new: 'nuevo',
  expected: 'esperado', counted: 'contado', difference: 'diferencia', mode: 'modalidad', role: 'rol', email: 'correo',
  display: 'visible', fragment: 'fragmento', format: 'formato', expires: 'vence', issued: 'emitido', pin: 'pin',
  numero: 'número', direccion: 'dirección', telefono: 'teléfono', credito: 'crédito', operacion: 'operación',
  ubicacion: 'ubicación', aplicacion: 'aplicación', authorization: 'autorización', authorized: 'autorizado',
  authorizer: 'autorizador', cancellation: 'anulación', cash: 'efectivo', category: 'categoría', correction: 'corrección',
  credit: 'crédito', days: 'días', inventory: 'inventario', layaway: 'apartado', movement: 'movimiento', order: 'pedido',
  original: 'original', percentage: 'porcentaje', permission: 'permiso', purchase: 'compra', reversal: 'reverso',
  shift: 'turno', supplier: 'proveedor', terminal: 'terminal', months: 'meses', count: 'cantidad', modified: 'modificado',
  fields: 'campos', first: 'primer', retail: 'venta', wholesale: 'mayorista', average: 'promedio', invoice: 'factura',
};

/**
 * Nombres completos que no se traducen bien palabra por palabra (pruebas del usuario del 26/11/2026: en Auditoría aparecía
 * «Modified fields: status | prefix | …»). La clave va en minúsculas y sin guiones.
 */
const PHRASES: Record<string, string> = {
  modifiedfields: 'Campos modificados', retailprice: 'Precio de venta', preciominorista: 'Precio de venta',
  previousretailprice: 'Precio de venta anterior', newretailprice: 'Precio de venta nuevo', wholesaleprice: 'Precio mayorista',
  previouswholesaleprice: 'Precio mayorista anterior', averagecost: 'Costo promedio', supplierdocumentnumber: 'Número de factura',
  invoicedate: 'Fecha de factura', vatmode: 'Modo de IVA', prefix: 'Prefijo', series: 'Serie', consecutive: 'Consecutivo',
  status: 'Estado', stock: 'Existencias', lines: 'Productos', supplier: 'Proveedor', authorizedby: 'Autorizado por',
  authorizedwith: 'Autorizado con', authorizationmode: 'Forma de autorización', cashafter: 'Efectivo después',
  cashbefore: 'Efectivo antes', cashdifference: 'Diferencia de efectivo', cashiermaxdiscountpercent: 'Descuento máximo del Cajero (%)',
  supervisormaxdiscountpercent: 'Descuento máximo del Supervisor (%)', changesource: 'Origen del cambio',
  changedbyself: 'Lo cambió el mismo usuario', commercialname: 'Nombre comercial', credentialrevoked: 'Carné revocado',
  defaultmonths: 'Meses por defecto', defaultdays: 'Días por defecto', displayname: 'Nombre', discountorigin: 'Origen del descuento',
  discountreasonpresent: 'Con motivo de descuento', discounttype: 'Tipo de descuento', documenttype: 'Tipo de documento',
  documentnumber: 'Número de documento', emailchanged: 'Cambió el correo', exceedslimit: 'Supera el cupo',
  expectedcash: 'Efectivo esperado', failedloginattempts: 'Intentos fallidos', firstnumber: 'Primer número',
  forgivenamount: 'Valor perdonado', fromclosedshift: 'De una caja ya cerrada', hasemail: 'Tiene correo',
  identification: 'Identificación', identifiedwith: 'Se identificó con', initialquantity: 'Cantidad inicial',
  kepthistory: 'Conserva su historial', lockcleared: 'Desbloqueado', newdays: 'Días nuevos',
  newenabled: 'Activado ahora', newpercentage: 'Porcentaje nuevo', orderstate: 'Estado del pedido', originalcount: 'Cantidad original',
  originaloperatingdate: 'Día de caja original', passwordreset: 'Contraseña restablecida', pathchanged: 'Cambió la ubicación',
  paymentmethods: 'Medios de pago', permissioncount: 'Permisos', previousdays: 'Días anteriores', previousenabled: 'Estaba activado',
  previouspercentage: 'Porcentaje anterior', previousrolecode: 'Rol anterior', pricetype: 'Tipo de precio', productcount: 'Productos',
  quantitydelta: 'Cambio de cantidad', receiptfooter: 'Pie del comprobante', receiptheader: 'Encabezado del comprobante',
  recoverymethod: 'Forma de recuperación', removedoverrides: 'Permisos individuales quitados', resellableconfirmed: 'Revendible',
  reversedamount: 'Valor revertido', reviewedcount: 'Revisados', blockriskycredit: 'Bloquear créditos riesgosos', blocked: 'Bloqueado',
  risk: 'Riesgo', saletype: 'Tipo de venta', sessionsinvalidated: 'Sesiones cerradas', totalcounted: 'Total contado',
  totaldifference: 'Diferencia total', totalexpected: 'Total esperado', username: 'Usuario', bootstrap: 'Instalación inicial',
  automatic: 'Automático', trigger: 'Origen', effect: 'Efecto', level: 'Nivel', exception: 'Excepción', failure: 'Falla',
  attempt: 'Intento', operation: 'Operación', from: 'Desde', to: 'Hasta', refunded: 'Devuelto', retained: 'Retenido',
  active: 'Activo', isactive: 'Activo', address: 'Dirección', phone: 'Teléfono', fullname: 'Nombre completo', name: 'Nombre',
  observations: 'Observaciones', creditlimit: 'Cupo de crédito', cupocredito: 'Cupo de crédito', email: 'Correo',
  nombrecompleto: 'Nombre completo', numerodocumento: 'Número de documento', tipodocumento: 'Tipo de documento',
  telefono: 'Teléfono', direccion: 'Dirección', observaciones: 'Observaciones', correo: 'Correo', category: 'Categoría',
  roles: 'Roles', role: 'Rol', products: 'Productos', sale: 'Venta', unitcost: 'Costo unitario', productcode: 'Código del producto',
  occurredon: 'Fecha', occurredat: 'Fecha y hora', productname: 'Producto', customername: 'Cliente', methodcode: 'Medio de pago',
};

/** «totalPaid» → «Total pagado», «FragmentoVisible» → «Fragmento visible». */
function fieldLabel(field: string): string {
  const phrase = PHRASES[field.replace(/[_-]+/g, '').toLowerCase()];
  if (phrase) return phrase;
  if (/^sessionconsumedby/i.test(field)) return 'Sesión de operador usada';
  const words = field.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').toLowerCase().split(' ').filter(Boolean)
    .map(word => WORDS[word] ?? word);
  const text = words.join(' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** Los campos modificados llegan como nombres internos («supplierDocumentNumber»); se muestran en palabras. */
function fieldList(value: unknown): string | null {
  if (!Array.isArray(value) || !value.every(item => typeof item === 'string')) return null;
  return value.map(item => fieldLabel(item as string)).join(', ');
}

/** Objects and lists as plain text («Cantidad: 2 · Monto: 1.000»), without braces or quotes. */
function plainText(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (Array.isArray(value)) return value.map(plainText).join(' | ');
  if (typeof value === 'object') {
    return Object.entries(value as Record<string, unknown>)
      .filter(([key, item]) => !HIDDEN_FIELD.test(key) && !(typeof item === 'string' && GUID.test(item)))
      .map(([key, item]) => `${fieldLabel(key)}: ${plainText(item)}`).join(' · ');
  }
  if (typeof value === 'boolean') return value ? 'Sí' : 'No';
  if (typeof value === 'string') return readableCode(value);
  return String(value);
}

/** Localize measured values for display, keeping identifiers and concurrency versions literal. */
function displayAuditValue(value: unknown, field = ''): unknown {
  const lastWord = field.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase().split(/[_-]/).at(-1) ?? '';
  if (/^(id|ids|code|codes|codigo|barcode|number|numero|version|phone|telephone|telefono|identification|identificacion|nit|document|documento|reference|referencia)$/.test(lastWord)) return value;
  if (typeof value === 'number') return groupedNumber(value);
  if (Array.isArray(value)) return value.map(item => displayAuditValue(item));
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, displayAuditValue(item, key)]));
  }
  return value;
}

/** One audit event as GET /api/audit-records returns it (only sanitized data, never secrets). */
export interface AuditRecord {
  id: string; occurredAtUtc: string; actorUserId: string | null; actorUserName: string | null;
  actionCode: string; entityType: string; entityId: string | null; details: string | null; correlationId: string | null;
  authorizerUserId: string | null; authorizerUserName: string | null; reason: string | null; result: string;
}

export interface AuditFilter { from: string; to: string; userId: string; action: string; page: number; pageSize: number; }

@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  /** Dates are local calendar days; the server expects UTC instants, so each day is sent from 00:00 to 23:59:59.999 local time. */
  records(filter: AuditFilter): Promise<Page<AuditRecord>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);
    if (filter.from) params = params.set('fromUtc', localDay(filter.from, false));
    if (filter.to) params = params.set('toUtc', localDay(filter.to, true));
    if (filter.userId) params = params.set('actorUserId', filter.userId);
    if (filter.action) params = params.set('action', filter.action);
    return firstValueFrom(this.http.get<Page<AuditRecord>>('/api/audit-records', { params }));
  }
}

export function localDay(iso: string, endOfDay: boolean): string {
  const [year, month, day] = iso.split('-').map(Number);
  const value = endOfDay ? new Date(year, month - 1, day, 23, 59, 59, 999) : new Date(year, month - 1, day, 0, 0, 0, 0);
  return value.toISOString();
}

function today(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

/** Read-only audit log (approved 16 November 2026): who did what, when, who authorized it and why. */
@Component({
  selector: 'cp-audit-log',
  imports: [DatePipe, FormsModule, MatPaginatorModule, SuggestField],
  templateUrl: './audit-log.html',
  styleUrls: ['../directory.scss'],
  styles: [`
    .directory-filters .audit-date { width: auto; min-width: 150px; }
    .directory-filters .audit-choice { width: min(100%, 290px); }
    .audit-filter-hint { margin: 0 0 10px; text-align: right; }
    .audit-detail td { background: var(--cp-surface-muted); }
    .audit-detail dl { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 6px 18px; margin: 0; }
    .audit-detail dl div { display: flex; flex-direction: column; min-width: 0; }
    .audit-detail dt { color: var(--cp-text-muted); font-size: 12px; }
    .audit-detail dd { margin: 0; overflow-wrap: anywhere; }
  `],
})
export class AuditLog implements OnInit {
  private readonly api = inject(AuditApi);
  private readonly admin = inject(AdminApi);
  readonly session = inject(SessionService);
  /** Each module's actions sorted by their Spanish name for the filter. */
  readonly groups = AUDIT_ACTION_GROUPS.map((group) => ({
    module: group.module,
    actions: Object.entries(group.actions).map(([code, label]) => ({ code, label })).sort((a, b) => a.label.localeCompare(b.label, 'es')),
  }));
  readonly records = signal<Page<AuditRecord> | null>(null);
  readonly users = signal<AdminUser[]>([]);
  /** Auditoría A.19: texto escrito o elegido en los filtros; se traduce al usuario o a la acción al buscar. */
  userText = ''; actionText = '';
  readonly userOptions = computed(() => this.users().map(user => ({ id: user.id, text: `${user.userName} · ${user.displayName}` })));
  /** Una acción con el mismo nombre en dos módulos lleva el módulo entre paréntesis para distinguirlas. */
  readonly actionOptions = (() => {
    const all = this.groups.flatMap(group => group.actions.map(action => ({ ...action, module: group.module })));
    const repeated = new Set(all.map(a => a.label).filter((label, index, labels) => labels.indexOf(label) !== index));
    return all.map(a => ({ code: a.code, module: a.module, text: repeated.has(a.label) ? `${a.label} (${a.module})` : a.label }));
  })();
  readonly userSuggestions = computed<SuggestOption[]>(() => this.userOptions().map(option => ({ value: option.id, text: option.text })));
  readonly actionSuggestions: SuggestOption[] = this.actionOptions.map(option => ({ value: option.code, text: option.text, hint: option.module }));
  /** Aviso junto a los filtros (no emergente) cuando lo escrito no basta para filtrar. */
  readonly filterHint = signal('');
  readonly expanded = signal<string | null>(null);
  readonly loading = signal(false);
  readonly error = toastSignal('error');
  filter: AuditFilter = { from: today(), to: today(), userId: '', action: '', page: 1, pageSize: 50 };
  private requestId = 0;

  /** The user filter needs the user list, which only USERS.READ may read. */
  get canFilterUsers(): boolean { return this.session.has('USERS.READ'); }

  ngOnInit(): void { void this.initialize(); }

  private async initialize(): Promise<void> {
    if (this.canFilterUsers) {
      try { this.users.set((await this.admin.users({ page: 1, pageSize: 100 })).items); }
      catch { this.users.set([]); }
    }
    await this.reload();
  }

  async reload(): Promise<void> {
    if (this.filter.from && this.filter.to && this.filter.from > this.filter.to) {
      this.error.set('La fecha inicial no puede ser posterior a la final.');
      return;
    }
    const id = ++this.requestId;
    this.loading.set(true); this.error.set(''); this.expanded.set(null);
    try {
      const data = await this.api.records(this.filter);
      if (id === this.requestId) this.records.set(data);
    } catch (e) { if (id === this.requestId) this.error.set(errorText(e)); }
    finally { if (id === this.requestId) this.loading.set(false); }
  }

  /** Elegido de la lista: filtra de una vez. Vacío es «todos». */
  pickUser(id: string): void { this.filterHint.set(''); this.filter.userId = id; this.filter.page = 1; void this.reload(); }
  pickAction(code: string): void { this.filterHint.set(''); this.filter.action = code; this.filter.page = 1; void this.reload(); }

  /** «Buscar» con texto escrito sin elegir: se usa si coincide con una sola opción; si no, se explica junto a los filtros. */
  find(): void {
    const userText = foldText(this.userText), actionText = foldText(this.actionText);
    const user = userText ? this.resolve(this.userSuggestions(), userText, text => foldText(text.split(' · ')[0])) : undefined;
    const action = actionText ? this.resolve(this.actionSuggestions, actionText) : undefined;
    if (userText && !user) { this.filterHint.set(`No hay un solo usuario con «${this.userText.trim()}». Elige uno de la lista o borra el texto para ver todos.`); return; }
    if (actionText && !action) {
      const count = suggestMatches(this.actionSuggestions, this.actionText, 1000).length;
      this.filterHint.set(count
        ? `Hay ${count} acciones con «${this.actionText.trim()}». Elige una de la lista o borra el texto para ver todas.`
        : `Ninguna acción tiene «${this.actionText.trim()}». Revisa lo escrito o borra el texto para ver todas.`);
      return;
    }
    this.filterHint.set('');
    this.filter.userId = user?.value ?? ''; this.filter.action = action?.value ?? '';
    if (user) this.userText = user.text;
    if (action) this.actionText = action.text;
    this.filter.page = 1; void this.reload();
  }
  /** Igual al texto (sin tildes), o la única opción que lo contiene. */
  private resolve(options: SuggestOption[], folded: string, alias?: (text: string) => string): SuggestOption | undefined {
    const exact = options.find(option => foldText(option.text) === folded || (alias && alias(option.text) === folded));
    if (exact) return exact;
    const found = suggestMatches(options, folded, 2);
    return found.length === 1 ? found[0] : undefined;
  }
  pageChanged(event: PageEvent): void { this.filter.page = event.pageIndex + 1; this.filter.pageSize = event.pageSize; void this.reload(); }
  toggle(record: AuditRecord): void { this.expanded.set(this.expanded() === record.id ? null : record.id); }

  actionLabel(record: AuditRecord): string { return auditActionLabel(record.actionCode); }
  moduleLabel(record: AuditRecord): string { return auditActionModule(record.actionCode); }
  /** Rejections and failures stand out; any other result reads as completed. */
  isProblem(record: AuditRecord): boolean { return /RECHAZ|FALL|ERROR|DENEG/i.test(record.result); }
  resultLabel(record: AuditRecord): string {
    const value = record.result.trim();
    return value ? value.charAt(0).toUpperCase() + value.slice(1).toLowerCase().replace(/_/g, ' ') : '';
  }
  entityLabel(record: AuditRecord): string {
    const known = ENTITIES[record.entityType];
    if (known) return known;
    const value = record.entityType.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/_/g, ' ').toLowerCase();
    return value.charAt(0).toUpperCase() + value.slice(1);
  }

  /** Details are sanitized JSON written by the server; shown as field and value, or as plain text if not JSON. */
  detailRows(record: AuditRecord): { field: string; value: string }[] {
    if (!record.details) return [];
    try {
      const parsed: unknown = JSON.parse(record.details);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        return Object.entries(parsed as Record<string, unknown>)
          .filter(([field, value]) => !HIDDEN_FIELD.test(field) && !(typeof value === 'string' && GUID.test(value)))
          .map(([field, value]) => ({ field: fieldLabel(field),
            value: (/fields$/i.test(field) ? fieldList(value) : null) ?? plainText(displayAuditValue(value, field)) }));
      }
      return [{ field: 'Detalle', value: typeof parsed === 'number' ? groupedNumber(parsed) : plainText(displayAuditValue(parsed)) }];
    } catch { return [{ field: 'Detalle', value: record.details }]; }
  }
}
