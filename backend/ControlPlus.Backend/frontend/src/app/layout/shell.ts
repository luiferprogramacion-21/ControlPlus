import { Component, inject, signal } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { SessionService } from '../core/session.service';
import { Icon } from './icon';

@Component({
  selector: 'cp-shell',
  imports: [RouterOutlet, RouterLink, Icon, A11yModule],
  template: `
    <a class="skip-link" href="#main-content">Saltar al contenido</a>
    <div class="app-shell app-shell--drawer">
      @if (menuOpen()) {
        <div class="drawer-backdrop" (click)="menuOpen.set(false)" aria-hidden="true"></div>
        <aside id="main-navigation" class="sidebar" role="dialog" aria-modal="true" aria-label="Navegación principal" cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown)="onMenuKeydown($event)">
          <div class="brand brand--sidebar"><img class="brand__mark brand__mark--logo" src="controlplus-icon.png" alt="" width="36" height="36"><span><strong>ControlPlus</strong><small>Baratísimo</small></span>
            <button class="icon-button icon-button--dark drawer-close" type="button" aria-label="Cerrar menú" (click)="menuOpen.set(false)" cdkFocusInitial><cp-icon name="close" /></button>
          </div>
          <p class="sidebar__label">NAVEGACIÓN</p>
          <nav class="sidebar__nav">
            @if (session.has('REPORTS.FINANCIAL_READ')) { <a routerLink="/operacion/dashboard" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'dashboard'"><span><cp-icon name="dashboard" /></span><b>Dashboard</b></a> }
            @if (session.has('SALES.CREATE')) { <a routerLink="/operacion/ventas" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'ventas'" aria-label="Punto de venta"><span><cp-icon name="sales" /></span><b>Ventas</b></a> }
            @if (session.has('SALES.OWN_READ') || session.has('SALES.ALL_READ')) { <a routerLink="/operacion/ventas/historial" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'historial-ventas'"><span><cp-icon name="sales" /></span><b>Historial de ventas</b></a> }
            @if (session.has('CASH.OWN_READ')) { <a routerLink="/operacion/caja" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'caja'" aria-label="Caja"><span><cp-icon name="cash" /></span><b>Caja</b></a> }
            @if (session.has('INVENTORY.READ')) { <a routerLink="/operacion/inventario" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'inventario'"><span><cp-icon name="inventory" /></span><b>Inventario</b></a> }
            @if (session.has('CUSTOMERS.BASIC_MANAGE') || session.has('CUSTOMERS.MANAGE')) { <a routerLink="/operacion/clientes" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'clientes'"><span><cp-icon name="customers" /></span><b>Clientes</b></a> }
            @if (session.has('CREDIT.MANAGE')) { <a routerLink="/operacion/creditos" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'creditos'"><span><cp-icon name="customers" /></span><b>Créditos y abonos</b></a> }
            @if (session.has('PURCHASES.RECEIVE') || session.has('PURCHASE_ORDERS.MANAGE')) { <a routerLink="/operacion/compras" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'compras'"><span><cp-icon name="purchases" /></span><b>Compras</b></a> }
            @if (session.has('SUPPLIERS.READ')) { <a routerLink="/operacion/proveedores" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'proveedores'"><span><cp-icon name="purchases" /></span><b>Proveedores</b></a> }
            @if (session.has('REPORTS.FINANCIAL_READ')) { <a routerLink="/operacion/reportes" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'reportes'"><span><cp-icon name="reports" /></span><b>Reportes</b></a> }
            @if (session.has('LAYAWAY.MANAGE')) { <a routerLink="/operacion/apartados" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'apartados'"><span><cp-icon name="catalog" /></span><b>Apartados</b></a> }
            @if (session.has('CONFIGURATION.MANAGE') || session.has('NUMBERING.MANAGE') || session.has('TAXES.MANAGE')) { <a routerLink="/operacion/configuracion" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'configuracion'"><span><cp-icon name="settings" /></span><b>Configuración</b></a> }
            @if (session.has('USERS.READ') || session.has('ROLES.READ')) { <a routerLink="/operacion/administracion" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'administracion'"><span><cp-icon name="settings" /></span><b>Administración</b></a> }
            @if (session.has('PRODUCTS.READ') || session.has('CATEGORIES.READ')) { <a routerLink="/operacion/catalogo" (click)="menuOpen.set(false)" class="nav-item" [class.nav-item--active]="activeSection() === 'catalogo'" aria-label="Catálogo"><span><cp-icon name="catalog" /></span><b>Catálogo</b></a> }
          </nav>
          <div class="sidebar__user"><span class="avatar">{{ session.user()?.displayName?.charAt(0) }}</span><span class="sidebar__user-copy"><strong>{{ session.user()?.displayName }}</strong><small>{{ session.user()?.roles?.join(', ') }}</small></span></div>
        </aside>
      }
      <section class="shell-content" [attr.inert]="menuOpen() ? '' : null">
        <header class="topbar">
          <div class="topbar__leading"><button class="icon-button menu-toggle" type="button" aria-label="Abrir menú" [attr.aria-expanded]="menuOpen()" [attr.aria-controls]="menuOpen() ? 'main-navigation' : null" (click)="menuOpen.set(true)"><cp-icon name="menu" /></button><div><h1>{{ pageTitle() }}</h1><p>{{ pageSubtitle() }}</p></div></div>
          <div class="topbar__status"><strong class="store-name">Baratísimo</strong><span>{{ session.user()?.displayName }}</span><button class="icon-button" type="button" (click)="logout()" aria-label="Cerrar sesión" title="Cerrar sesión"><cp-icon name="lock" /></button></div>
        </header>
        <main id="main-content" class="workspace" [class.workspace--bounded]="boundedWorkspace()" tabindex="-1"><router-outlet /></main>
      </section>
    </div>`,
})
export class Shell {
  readonly session = inject(SessionService);
  readonly menuOpen = signal(false);
  readonly boundedWorkspace = signal(false);
  readonly pageTitle = signal('Caja');
  readonly pageSubtitle = signal('Apertura de turno');
  readonly activeSection = signal<'caja' | 'ventas' | 'historial-ventas' | 'catalogo' | 'inventario' | 'clientes' | 'creditos' | 'proveedores' | 'compras' | 'administracion' | 'configuracion' | 'apartados' | 'reportes' | 'dashboard'>('caja');
  private readonly router = inject(Router);

  constructor() {
    this.syncHeader(this.router.url);
    this.router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(event => { this.menuOpen.set(false); this.syncHeader(event.urlAfterRedirects); });
  }

  logout(): void { this.session.logout(); void this.router.navigateByUrl('/ingresar'); }
  onMenuKeydown(event: KeyboardEvent): void { if (event.key === 'Escape') { event.preventDefault(); this.menuOpen.set(false); } }
  private syncHeader(url: string): void {
    this.boundedWorkspace.set(url.endsWith('/ventas'));
    if (url.includes('/dashboard')) { this.activeSection.set('dashboard'); this.pageTitle.set('Dashboard'); this.pageSubtitle.set('Resumen operativo de hoy'); return; }
    if (url.includes('/ventas/historial')) { this.activeSection.set('historial-ventas'); this.pageTitle.set('Historial de ventas'); this.pageSubtitle.set('Consulta, detalle y anulación'); return; }
    if (url.includes('/ventas')) { this.activeSection.set('ventas'); this.pageTitle.set('Punto de venta'); this.pageSubtitle.set('Terminal de ventas · Venta en curso'); return; }
    if (url.includes('/caja')) { this.activeSection.set('caja'); this.pageTitle.set('Caja'); this.pageSubtitle.set(url.includes('cierre') ? 'Cierre y conciliación' : 'Apertura y movimientos'); return; }
    if (url.includes('/inventario')) { this.activeSection.set('inventario'); this.pageTitle.set('Inventario'); this.pageSubtitle.set('Existencias y movimientos'); return; }
    if (url.includes('/clientes')) { this.activeSection.set('clientes'); this.pageTitle.set('Clientes'); this.pageSubtitle.set('Consulta y gestión'); return; }
    if (url.includes('/compras')) { this.activeSection.set('compras'); this.pageTitle.set('Compras'); this.pageSubtitle.set('Pedidos a proveedores y recepción'); return; }
    if (url.includes('/proveedores')) { this.activeSection.set('proveedores'); this.pageTitle.set('Proveedores'); this.pageSubtitle.set('Directorio y gestión'); return; }
    if (url.includes('/reportes')) { this.activeSection.set('reportes'); this.pageTitle.set('Reportes'); this.pageSubtitle.set('Indicadores y análisis operativo'); return; }
    if (url.includes('/apartados')) { this.activeSection.set('apartados'); this.pageTitle.set('Apartados'); this.pageSubtitle.set('Abonos, entregas y saldos'); return; }
    if (url.includes('/configuracion')) { this.activeSection.set('configuracion'); this.pageTitle.set('Configuración'); this.pageSubtitle.set('Numeración, plazos, impresora y motivos'); return; }
    if (url.includes('/soporte/restauracion')) { this.activeSection.set('administracion'); this.pageTitle.set('Restauración'); this.pageSubtitle.set('Administración / Soporte / Respaldos / Restaurar'); return; }
    if (url.includes('/soporte')) { this.activeSection.set('administracion'); this.pageTitle.set('Soporte y continuidad'); this.pageSubtitle.set('Salud del sistema, respaldos y diagnóstico'); return; }
    if (url.includes('/administracion')) { this.activeSection.set('administracion'); this.pageTitle.set('Administración'); this.pageSubtitle.set('Usuarios, roles y permisos'); return; }
    if (url.includes('/creditos')) { this.activeSection.set('creditos'); this.pageTitle.set('Créditos y abonos'); this.pageSubtitle.set('Cartera de clientes y pagos'); return; }
    this.activeSection.set('catalogo'); this.pageTitle.set('Catálogo'); this.pageSubtitle.set('Productos, categorías y precios');
  }
}
