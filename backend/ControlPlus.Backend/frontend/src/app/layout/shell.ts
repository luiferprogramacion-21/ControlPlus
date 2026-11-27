import { Component, computed, ElementRef, HostListener, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { SessionService } from '../core/session.service';
import { OperatorPresence } from '../core/operator-presence';
import { CashApi } from '../features/cash/cash-api';
import { EstablishmentService } from '../core/establishment';
import { DisplayScaleService } from '../core/display-scale';
import { WindowsShortcuts } from '../core/windows-shortcuts';
import { RowActions } from '../core/row-actions';
import { ListHelpers } from '../core/list-helpers';
import { WindowSheet } from '../shared/window-sheet';
import { Icon } from './icon';
import { paginatorIntlProvider } from './paginator-intl';

type Section = 'inicio' | 'cambios' | 'caja' | 'ventas' | 'historial-ventas' | 'catalogo' | 'etiquetas' | 'inventario' | 'clientes' | 'creditos' | 'proveedores' | 'compras' | 'recepciones' | 'administracion' | 'configuracion' | 'apartados' | 'reportes' | 'dashboard';
interface NavItem { section: Section; label: string; link: string; queryParams?: Record<string, string>; icon: string; visible: (session: SessionService) => boolean; }
interface NavGroup { label: string; items: NavItem[]; }

/**
 * Menú siempre visible (decisión del 19 de noviembre de 2026): fijo a la izquierda como el panel de
 * navegación de Windows, agrupado por secciones, con un ícono propio por pantalla y plegable a solo íconos.
 * Cada equipo recuerda si lo dejó plegado.
 */
const NAVIGATION: NavGroup[] = [
  { label: 'Resumen', items: [
    { section: 'inicio', label: 'Inicio', link: '/operacion/inicio', icon: 'home', visible: () => true },
    { section: 'dashboard', label: 'Dashboard', link: '/operacion/dashboard', icon: 'dashboard', visible: s => s.has('REPORTS.FINANCIAL_READ') },
    { section: 'reportes', label: 'Reportes', link: '/operacion/reportes', icon: 'reports', visible: s => s.has('REPORTS.FINANCIAL_READ') },
  ] },
  { label: 'Ventas y caja', items: [
    { section: 'ventas', label: 'Punto de venta', link: '/operacion/ventas', icon: 'sales', visible: s => s.has('SALES.CREATE') },
    { section: 'historial-ventas', label: 'Historial de ventas', link: '/operacion/ventas/historial', icon: 'history', visible: s => s.has('SALES.OWN_READ') || s.has('SALES.ALL_READ') },
    { section: 'cambios', label: 'Cambios y devoluciones', link: '/operacion/ventas/historial', queryParams: { accion: 'cambio' }, icon: 'exchange', visible: s => s.has('SALES.CREATE') && (s.has('SALES.OWN_READ') || s.has('SALES.ALL_READ')) },
    { section: 'caja', label: 'Caja', link: '/operacion/caja', icon: 'cash', visible: s => s.has('CASH.OWN_READ') },
    { section: 'apartados', label: 'Apartados', link: '/operacion/apartados', icon: 'layaway', visible: s => s.has('LAYAWAY.MANAGE') },
    { section: 'creditos', label: 'Créditos y abonos', link: '/operacion/creditos', icon: 'credit', visible: s => s.has('CREDIT.MANAGE') },
    { section: 'clientes', label: 'Clientes', link: '/operacion/clientes', icon: 'customers', visible: s => s.has('CUSTOMERS.BASIC_MANAGE') || s.has('CUSTOMERS.MANAGE') },
  ] },
  { label: 'Productos', items: [
    { section: 'catalogo', label: 'Catálogo', link: '/operacion/catalogo', icon: 'catalog', visible: s => s.has('PRODUCTS.READ') || s.has('CATEGORIES.READ') },
    { section: 'inventario', label: 'Inventario', link: '/operacion/inventario', icon: 'inventory', visible: s => s.has('INVENTORY.READ') },
    { section: 'etiquetas', label: 'Etiquetas', link: '/operacion/etiquetas', icon: 'labels', visible: s => s.has('PRODUCTS.LABELS_MANAGE') },
  ] },
  { label: 'Compras', items: [
    { section: 'compras', label: 'Compras', link: '/operacion/compras', icon: 'purchases', visible: s => s.has('PURCHASES.RECEIVE') || s.has('PURCHASE_ORDERS.MANAGE') },
    { section: 'proveedores', label: 'Proveedores', link: '/operacion/proveedores', icon: 'supplier', visible: s => s.has('SUPPLIERS.READ') },
  ] },
  { label: 'Sistema', items: [
    { section: 'configuracion', label: 'Configuración', link: '/operacion/configuracion', icon: 'settings', visible: s => s.has('CONFIGURATION.MANAGE') || s.has('CASH.SHIFT_MODE_MANAGE') },
    { section: 'administracion', label: 'Administración', link: '/operacion/administracion', icon: 'admin', visible: s => s.has('USERS.READ') || s.has('ROLES.READ') || s.has('AUDIT.READ') },
  ] },
];

const COLLAPSED_STORAGE_KEY = 'controlplus.sidebarCollapsed';
function readCollapsed(): boolean { try { return localStorage.getItem(COLLAPSED_STORAGE_KEY) === 'true'; } catch { return false; } }

@Component({
  selector: 'cp-shell',
  imports: [RouterOutlet, RouterLink, Icon, WindowSheet],
  // La traducción del paginador vive aquí y no en app.config: así el paginador (con sus menús, tooltips y
  // formularios) no entra en el paquete inicial que descarga la pantalla de ingreso.
  providers: [paginatorIntlProvider],
  template: `
    <a class="skip-link" href="#main-content">Saltar al contenido</a>
    <div class="app-shell app-shell--fixed" [class.app-shell--collapsed]="collapsed()">
      <aside id="main-navigation" class="sidebar" aria-label="Navegación principal">
        <div class="brand brand--sidebar"><img class="brand__mark brand__mark--logo" src="controlplus-icon.png" alt="" width="36" height="36"><span class="sidebar__text"><strong>ControlPlus</strong><small>{{ establishment.name() }}</small></span></div>
        <nav class="sidebar__nav">
          @for (group of groups(); track group.label) {
            <p class="sidebar__label">{{ group.label }}</p>
            @for (item of group.items; track item.section) {
              <a [routerLink]="isOff(item) ? null : item.link" [queryParams]="item.queryParams ?? null" class="nav-item" [class.nav-item--active]="activeSection() === item.section" [class.nav-item--off]="isOff(item)" [attr.aria-disabled]="isOff(item) || null" [attr.tabindex]="isOff(item) ? -1 : null" [attr.aria-current]="activeSection() === item.section ? 'page' : null" [attr.aria-label]="offLabel(item)" [title]="offLabel(item)"><span><cp-icon [name]="item.icon" /></span><b>{{ item.label }}</b></a>
            }
          }
        </nav>
        <div class="sidebar__user">
          @if (userMenuOpen()) {
            <div class="user-menu" id="user-menu" role="menu" aria-label="Opciones de la cuenta" (keydown.escape)="closeUserMenuAndFocus()">
              <p class="user-menu__heading" id="display-scale-heading"><cp-icon name="text-size" /><span>Tamaño de la pantalla</span></p>
              <div class="user-menu__scale" role="group" aria-label="Tamaño libre">
                <button class="user-menu__step" type="button" (click)="displayScale.decrease()" aria-label="Más pequeño (Ctrl −)" title="Más pequeño (Ctrl −)">−</button>
                <strong aria-live="polite">{{ displayScale.percent() }}</strong>
                <button class="user-menu__step" type="button" (click)="displayScale.increase()" aria-label="Más grande (Ctrl +)" title="Más grande (Ctrl +)">+</button>
              </div>
              <div role="group" aria-labelledby="display-scale-heading">
                @for (option of displayScale.options; track option.value) {
                  <button class="user-menu__option" type="button" role="menuitemradio" [attr.aria-checked]="displayScale.scale() === option.value" (click)="displayScale.set(option.value)">
                    <span class="user-menu__check">@if (displayScale.scale() === option.value) { <cp-icon name="check" /> }</span><span>{{ option.label }}</span>
                  </button>
                }
              </div>
              <hr class="user-menu__separator">
              <button class="user-menu__option" type="button" role="menuitem" (click)="openShortcuts()"><span class="user-menu__check"><cp-icon name="keyboard" /></span><span>Atajos del teclado (F1)</span></button>
              <button class="user-menu__item" type="button" role="menuitem" (click)="logout()"><cp-icon name="logout" /><span>Cerrar sesión</span></button>
            </div>
          }
          <button class="sidebar__user-button" type="button" aria-haspopup="menu" [attr.aria-expanded]="userMenuOpen()" aria-controls="user-menu" [title]="'Cuenta de ' + (session.user()?.displayName ?? '') + ': tamaño de la pantalla y cerrar sesión'" (click)="userMenuOpen.update(open => !open)" (keydown.escape)="userMenuOpen.set(false)"><span class="avatar">{{ session.user()?.displayName?.charAt(0) }}</span><span class="sidebar__user-copy"><strong>{{ session.user()?.displayName }}</strong><small>{{ session.user()?.roles?.join(', ') }}</small></span><span class="sidebar__user-caret" aria-hidden="true">⌃</span></button>
        </div>
      </aside>
      <section class="shell-content">
        <header class="topbar">
          <div class="topbar__leading"><button class="icon-button menu-toggle" type="button" [attr.aria-label]="collapsed() ? 'Mostrar el menú completo' : 'Plegar el menú'" [title]="collapsed() ? 'Mostrar el menú completo' : 'Plegar el menú'" [attr.aria-expanded]="!collapsed()" aria-controls="main-navigation" (click)="toggleCollapsed()"><cp-icon name="menu" /></button><div><h1>{{ pageTitle() }}</h1><p>{{ pageSubtitle() }}</p></div></div>
          <div class="topbar__status"><strong class="store-name">{{ establishment.name() }}</strong>@if (operator.active(); as active) { <span class="operator-chip" role="status">Operador: {{ active.name }}@if (operator.roleLabel()) { · {{ operator.roleLabel() }} }</span> }<span>{{ session.user()?.displayName }}</span><button class="icon-button" type="button" (click)="logout()" aria-label="Cerrar sesión" title="Cerrar sesión"><cp-icon name="logout" /></button></div>
        </header>
        <main id="main-content" class="workspace" [class.workspace--bounded]="boundedWorkspace()" tabindex="-1"><router-outlet /></main>
      </section>
    </div>
    @if (shortcutsOpen()) {
      <div class="module-form-sheet" role="dialog" aria-modal="true" aria-labelledby="shortcuts-title"><div class="module-form-sheet__backdrop" (click)="shortcutsOpen.set(false)"></div>
        <section class="panel panel--padded module-form">
          <div class="panel-heading"><h2 id="shortcuts-title">Atajos del teclado</h2><p>Funcionan como en Windows en todas las pantallas.</p></div>
          <dl class="shortcut-list">
            @for (shortcut of shortcuts; track shortcut.keys) { <div><dt>@for (key of shortcut.keys.split('+'); track $index) { @if ($index) { + } <kbd>{{ key }}</kbd> }</dt><dd>{{ shortcut.action }}</dd></div> }
          </dl>
          <div class="form-actions"><button class="button button--primary" type="button" (click)="shortcutsOpen.set(false)" autofocus>Aceptar</button></div>
        </section>
      </div>
    }`,
})
export class Shell implements OnInit {
  readonly session = inject(SessionService);
  readonly establishment = inject(EstablishmentService);
  readonly displayScale = inject(DisplayScaleService);
  readonly collapsed = signal(readCollapsed());
  /** Menú del perfil de abajo de la barra lateral: tamaño de la pantalla y cerrar sesión. */
  readonly userMenuOpen = signal(false);
  readonly shortcutsOpen = signal(false);
  readonly shortcuts = [
    { keys: 'Enter', action: 'Aceptar, guardar o buscar lo escrito.' },
    { keys: 'Esc', action: 'Cancelar o cerrar la ventana.' },
    { keys: 'Ctrl+B', action: 'Ir al campo de búsqueda de la pantalla.' },
    { keys: 'F5', action: 'Actualizar la lista de la pantalla.' },
    { keys: 'Tab', action: 'Pasar al siguiente campo (Mayús+Tab, al anterior).' },
    { keys: 'Doble clic', action: 'Abrir una fila de una lista.' },
    { keys: 'F1', action: 'Ver esta lista de atajos.' },
    { keys: 'Ctrl + / Ctrl −', action: 'Agrandar o achicar la pantalla (Ctrl 0 vuelve a 125 %).' },
    { keys: 'F2', action: 'Punto de venta: buscar o escanear un producto.' },
    { keys: 'F3', action: 'Punto de venta: buscar o crear el cliente.' },
    { keys: 'F4', action: 'Punto de venta: cambiar la cantidad.' },
    { keys: 'Supr', action: 'Punto de venta: quitar el producto seleccionado.' },
    { keys: 'F6', action: 'Punto de venta: poner la venta en espera para atender a otro cliente.' },
    { keys: 'F7', action: 'Punto de venta: ver las ventas en espera y retomar una.' },
    { keys: 'F9', action: 'Punto de venta: cobrar y confirmar la venta.' },
    { keys: 'Alt+1 a Alt+4', action: 'Al cobrar: sumar un billete de 10.000, 20.000, 50.000 o 100.000 (Alt+E exacto, Alt+B borrar).' },
  ];
  private readonly host = inject(ElementRef<HTMLElement>);
  readonly boundedWorkspace = signal(false);
  readonly pageTitle = signal('Caja');
  readonly pageSubtitle = signal('Apertura de turno');
  readonly activeSection = signal<Section>('caja');
  /** Solo las secciones con alguna pantalla permitida para el usuario. */
  readonly groups = computed(() => {
    this.session.user();
    return NAVIGATION.map(group => ({ label: group.label, items: group.items.filter(item => item.visible(this.session)) })).filter(group => group.items.length);
  });
  private readonly router = inject(Router);
  /** Opción B del turno compartido (26/11/2026): quien pasó su carné; el menú apaga lo que esa persona no puede usar. */
  readonly operator = inject(OperatorPresence);
  private readonly cash = inject(CashApi);
  isOff(item: NavItem): boolean {
    return !!this.operator.active() && !item.visible({ has: (code: string) => this.operator.has(code) } as SessionService);
  }
  offLabel(item: NavItem): string {
    const active = this.operator.active();
    return active && this.isOff(item) ? `${item.label}: ${active.name} no puede usarlo. Termina su venta o su operación para volver a tu menú.` : item.label;
  }

  constructor() {
    inject(WindowsShortcuts).start();
    inject(RowActions).start();
    inject(ListHelpers).start();
    this.syncHeader(this.router.url);
    this.router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(event => {
        this.userMenuOpen.set(false); this.syncHeader(event.urlAfterRedirects);
        // Si alguien tenía su carné activo, se revisa al cambiar de pantalla si ya terminó.
        if (this.operator.active() && this.session.has('CASH.OWN_READ')) void this.cash.state().catch(() => undefined);
      });
  }

  ngOnInit(): void { void this.establishment.load().catch(() => undefined); }

  toggleCollapsed(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    try { localStorage.setItem(COLLAPSED_STORAGE_KEY, String(next)); } catch { /* Sin almacenamiento: dura hasta recargar. */ }
  }

  openShortcuts(): void { this.userMenuOpen.set(false); this.shortcutsOpen.set(true); }
  /** F1, como la ayuda de Windows: muestra los atajos del teclado. */
  @HostListener('document:keydown', ['$event'])
  onHelpKey(event: KeyboardEvent): void {
    // Tamaño libre como en Windows: Ctrl + agranda, Ctrl − achica y Ctrl 0 vuelve a 125 %.
    if ((event.ctrlKey || event.metaKey) && !event.altKey) {
      if (event.key === '+' || event.key === '=') { event.preventDefault(); this.displayScale.increase(); }
      else if (event.key === '-') { event.preventDefault(); this.displayScale.decrease(); }
      else if (event.key === '0') { event.preventDefault(); this.displayScale.reset(); }
      return;
    }
    if (event.key !== 'F1' || event.altKey || event.metaKey) return;
    event.preventDefault();
    this.openShortcuts();
  }
  logout(): void { this.userMenuOpen.set(false); this.session.logout(); void this.router.navigateByUrl('/ingresar'); }
  closeUserMenuAndFocus(): void {
    this.userMenuOpen.set(false);
    ((this.host.nativeElement as HTMLElement).querySelector('.sidebar__user-button') as HTMLElement | null)?.focus();
  }
  /** Un toque fuera del perfil cierra su menú. */
  @HostListener('document:click', ['$event'])
  closeUserMenu(event: MouseEvent): void {
    if (!this.userMenuOpen()) return;
    const profile = (this.host.nativeElement as HTMLElement).querySelector('.sidebar__user');
    if (profile && !profile.contains(event.target as Node)) this.userMenuOpen.set(false);
  }
  private syncHeader(url: string): void {
    this.boundedWorkspace.set(url.endsWith('/ventas'));
    if (url.includes('/inicio')) { this.activeSection.set('inicio'); this.pageTitle.set('Inicio'); this.pageSubtitle.set('Tareas frecuentes'); return; }
    if (url.includes('/dashboard')) { this.activeSection.set('dashboard'); this.pageTitle.set('Dashboard'); this.pageSubtitle.set('Resumen operativo de hoy'); return; }
    if (url.includes('/ventas/historial') && url.includes('accion=cambio')) { this.activeSection.set('cambios'); this.pageTitle.set('Cambios y devoluciones'); this.pageSubtitle.set('Busca la venta por su número'); return; }
    if (url.includes('/ventas/historial')) { this.activeSection.set('historial-ventas'); this.pageTitle.set('Historial de ventas'); this.pageSubtitle.set('Consulta, detalle y anulación'); return; }
    if (url.includes('/ventas')) { this.activeSection.set('ventas'); this.pageTitle.set('Punto de venta'); this.pageSubtitle.set('Terminal de ventas · Venta en curso'); return; }
    if (url.includes('/caja')) { this.activeSection.set('caja'); this.pageTitle.set('Caja'); this.pageSubtitle.set(url.includes('cierre') ? 'Cierre y conciliación' : 'Apertura y movimientos'); return; }
    if (url.includes('/inventario')) { this.activeSection.set('inventario'); this.pageTitle.set('Inventario'); this.pageSubtitle.set('Existencias y movimientos'); return; }
    if (url.includes('/etiquetas')) { this.activeSection.set('etiquetas'); this.pageTitle.set('Etiquetas de productos'); this.pageSubtitle.set('Selección, vista previa e impresión'); return; }
    if (url.includes('/clientes')) { this.activeSection.set('clientes'); this.pageTitle.set('Clientes'); this.pageSubtitle.set('Consulta y gestión'); return; }
    if (url.includes('/recepciones')) { this.activeSection.set('compras'); this.pageTitle.set('Compras'); this.pageSubtitle.set('Mercancía recibida de los pedidos'); return; }
    if (url.includes('/compras')) { this.activeSection.set('compras'); this.pageTitle.set('Compras'); this.pageSubtitle.set('Pedidos al proveedor, mercancía recibida y compras sin pedido'); return; }
    if (url.includes('/proveedores')) { this.activeSection.set('proveedores'); this.pageTitle.set('Proveedores'); this.pageSubtitle.set('Directorio y gestión'); return; }
    if (url.includes('/reportes')) { this.activeSection.set('reportes'); this.pageTitle.set('Reportes'); this.pageSubtitle.set('Indicadores y análisis operativo'); return; }
    if (url.includes('/apartados')) { this.activeSection.set('apartados'); this.pageTitle.set('Apartados'); this.pageSubtitle.set('Abonos, entregas y saldos'); return; }
    if (url.includes('/configuracion')) { this.activeSection.set('configuracion'); this.pageTitle.set('Configuración'); this.pageSubtitle.set('Establecimiento, caja, plazos e impresión'); return; }
    if (url.includes('/soporte/restauracion')) { this.activeSection.set('administracion'); this.pageTitle.set('Restauración'); this.pageSubtitle.set('Administración / Soporte / Respaldos / Restaurar'); return; }
    if (url.includes('/soporte')) { this.activeSection.set('administracion'); this.pageTitle.set('Soporte y continuidad'); this.pageSubtitle.set('Salud del sistema, respaldos y diagnóstico'); return; }
    if (url.includes('/administracion')) { this.activeSection.set('administracion'); this.pageTitle.set('Administración'); this.pageSubtitle.set('Usuarios, roles, permisos y auditoría'); return; }
    if (url.includes('/creditos')) { this.activeSection.set('creditos'); this.pageTitle.set('Créditos y abonos'); this.pageSubtitle.set('Cartera de clientes y pagos'); return; }
    this.activeSection.set('catalogo'); this.pageTitle.set('Catálogo'); this.pageSubtitle.set('Productos, categorías y precios');
  }
}
