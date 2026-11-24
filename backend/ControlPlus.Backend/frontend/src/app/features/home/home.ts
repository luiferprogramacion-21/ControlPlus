import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionService } from '../../core/session.service';
import { CashApi, CashState } from '../cash/cash-api';
import { Icon } from '../../layout/icon';

export interface HomeTask { id: string; title: string; description: string; icon: string; link: string; queryParams?: Record<string, string>; primary?: boolean; }

/**
 * Tareas de Inicio según permisos y estado de Caja. Lo que la persona no puede hacer no aparece.
 * Con la caja cerrada, «Abrir caja» va primero y «Vender» explica que antes hay que abrirla.
 */
export function homeTasks(can: (permission: string) => boolean, cash: CashState | null, userId: string | null = null): HomeTask[] {
  const tasks: HomeTask[] = [];
  const cashOpen = !!cash?.hasOpenShift && !cash.requiresPreviousShiftClosure;
  const manageCash = can('CASH.SHIFT_MANAGE');
  // Auditoría del 23/11/2026 (C.1): quien vende (el Cajero) abre la caja con autorización y cierra su propio turno.
  const opensCash = manageCash || (can('CASH.OWN_READ') && can('SALES.CREATE'));
  const ownsShift = !!userId && cash?.currentShift?.mode === 'INDIVIDUAL' && cash.currentShift.responsibleUserId === userId;
  if (opensCash && cash && cash.isConfigured && !cash.hasOpenShift) tasks.push({ id: 'abrir-caja', title: 'Abrir caja', description: 'Empieza el turno con el dinero inicial.', icon: 'cash', link: '/operacion/caja', primary: true });
  if (manageCash && cash?.requiresPreviousShiftClosure) tasks.push({ id: 'cerrar-anterior', title: 'Cerrar el turno anterior', description: 'Quedó abierto un turno de otro día.', icon: 'cash', link: '/operacion/caja', primary: true });
  if (can('SALES.CREATE')) tasks.push({ id: 'vender', title: 'Vender', description: cash && !cashOpen ? 'Primero abre la caja.' : 'Registrar una venta y cobrar.', icon: 'sales', link: '/operacion/ventas', primary: cashOpen || !cash });
  if (can('SALES.OWN_READ') || can('SALES.ALL_READ')) tasks.push({ id: 'cambio', title: 'Cambiar o devolver', description: 'Cambio de productos de una venta.', icon: 'history', link: '/operacion/ventas/historial', queryParams: { accion: 'cambio' } });
  if (can('CREDIT.MANAGE')) tasks.push({ id: 'abono', title: 'Recibir abono', description: 'Pago de un cliente a su crédito.', icon: 'credit', link: '/operacion/creditos' });
  if (can('LAYAWAY.MANAGE')) tasks.push({ id: 'apartado', title: 'Nuevo apartado', description: 'Separar productos con un pago inicial.', icon: 'layaway', link: '/operacion/apartados', queryParams: { accion: 'nuevo' } });
  if (can('PRODUCTS.READ')) tasks.push({ id: 'producto', title: 'Buscar producto', description: 'Precio, código y existencias.', icon: 'catalog', link: '/operacion/catalogo' });
  if (can('CUSTOMERS.BASIC_MANAGE') || can('CUSTOMERS.MANAGE')) tasks.push({ id: 'clientes', title: 'Clientes', description: 'Buscar o registrar un cliente.', icon: 'customers', link: '/operacion/clientes' });
  if (can('PURCHASES.RECEIVE')) tasks.push({ id: 'mercancia', title: 'Recibir mercancía', description: 'Registrar lo que llegó del proveedor.', icon: 'reception', link: '/operacion/compras', queryParams: { accion: 'registrar' } });
  if (can('INVENTORY.READ')) tasks.push({ id: 'inventario', title: 'Inventario', description: 'Existencias y productos por agotarse.', icon: 'inventory', link: '/operacion/inventario' });
  if ((manageCash || ownsShift) && cashOpen) tasks.push({ id: 'cerrar-caja', title: 'Cerrar caja', description: 'Contar el efectivo y terminar el turno.', icon: 'cash', link: '/operacion/caja' });
  if (can('REPORTS.FINANCIAL_READ')) tasks.push({ id: 'resumen', title: 'Resumen del día', description: 'Ventas, caja y alertas de hoy.', icon: 'dashboard', link: '/operacion/dashboard' });
  return tasks;
}

/** Pantalla de Inicio (decisión del 19 de noviembre de 2026): botones grandes con las tareas frecuentes del rol. */
@Component({
  selector: 'cp-home',
  imports: [RouterLink, Icon, DatePipe],
  template: `
    <section class="home">
      <header class="home__greeting">
        <h2>Hola, {{ firstName() }}</h2>
        <p>¿Qué quieres hacer?</p>
      </header>
      @if (session.has('CASH.OWN_READ')) {
        <p class="home__cash" [class.home__cash--open]="cashOpen()" role="status">
          <cp-icon name="cash" />
          @if (loadingCash()) { Consultando la caja… }
          @else if (!cash()) { No se pudo consultar la caja. }
          @else if (!cash()!.isConfigured) { La caja aún no está configurada. }
          @else if (cash()!.requiresPreviousShiftClosure) { Hay un turno de otro día sin cerrar. }
          @else if (cashOpen()) { Caja abierta @if (cash()!.currentShift?.openedAtUtc; as openedAt) { desde las {{ openedAt | date:'h:mm a' }} } }
          @else { La caja está cerrada. }
        </p>
      }
      <nav class="home__tasks" aria-label="Tareas frecuentes">
        @for (task of tasks(); track task.id) {
          <a class="home-task" [class.home-task--primary]="task.primary" [routerLink]="task.link" [queryParams]="task.queryParams ?? null">
            <span class="home-task__icon"><cp-icon [name]="task.icon" /></span>
            <span class="home-task__text"><strong>{{ task.title }}</strong><small>{{ task.description }}</small></span>
          </a>
        } @empty {
          <p class="muted">Tu usuario no tiene tareas asignadas. Pide al Administrador que revise tus permisos.</p>
        }
      </nav>
      <p class="home__tip">Consejo: pulsa <kbd>F1</kbd> para ver los atajos del teclado.</p>
    </section>`,
  styles: [`
    .home { display:flex; flex-direction:column; gap:18px; max-width:1100px; margin:0 auto; padding:8px 4px 16px; }
    .home__greeting h2 { margin:0 0 2px; font-size:28px; font-weight:600; }
    .home__greeting p { margin:0; color:var(--cp-text-secondary); font-size:17px; }
    .home__cash { display:flex; align-items:center; gap:10px; width:fit-content; margin:0; padding:10px 16px; border-radius:10px; background:var(--cp-warning-soft); color:var(--cp-warning); font-weight:600; }
    .home__cash--open { background:var(--cp-success-soft); color:var(--cp-success); }
    .home__tasks { display:grid; grid-template-columns:repeat(auto-fill, minmax(250px, 1fr)); gap:14px; }
    .home-task { display:flex; align-items:center; gap:14px; min-height:96px; padding:16px 18px; border:1px solid var(--cp-border); border-radius:14px; background:var(--cp-surface); color:var(--cp-text); text-decoration:none; box-shadow:0 1px 2px rgb(36 49 66 / 4%); transition:border-color .12s ease, background-color .12s ease; }
    .home-task:hover, .home-task:focus-visible { border-color:var(--cp-primary); background:var(--cp-primary-soft); }
    .home-task__icon { display:grid; place-items:center; flex:0 0 52px; height:52px; border-radius:12px; background:var(--cp-primary-soft); color:var(--cp-primary); }
    .home-task__text { display:flex; flex-direction:column; gap:4px; }
    .home-task__text strong { font-size:19px; font-weight:600; }
    .home-task__text small { color:var(--cp-text-secondary); font-size:14px; line-height:1.35; }
    .home-task--primary { border-color:var(--cp-primary); background:var(--cp-primary); color:white; }
    .home-task--primary .home-task__icon { background:rgb(255 255 255 / 18%); color:white; }
    .home-task--primary .home-task__text small { color:#dbe7f5; }
    .home-task--primary:hover, .home-task--primary:focus-visible { background:var(--cp-primary-hover); }
    .home__tip { margin:0; color:var(--cp-text-muted); font-size:14px; }
  `],
})
export class Home implements OnInit {
  readonly session = inject(SessionService);
  private readonly cashApi = inject(CashApi);
  readonly cash = signal<CashState | null>(null);
  readonly loadingCash = signal(false);
  readonly cashOpen = computed(() => !!this.cash()?.hasOpenShift && !this.cash()?.requiresPreviousShiftClosure);
  readonly firstName = computed(() => (this.session.user()?.displayName ?? '').trim().split(/\s+/)[0] || 'de nuevo');
  readonly tasks = computed(() => homeTasks(permission => this.session.has(permission), this.session.has('CASH.OWN_READ') ? this.cash() : null, this.session.user()?.id ?? null));

  async ngOnInit(): Promise<void> {
    if (!this.session.has('CASH.OWN_READ')) return;
    this.loadingCash.set(true);
    try { this.cash.set(await this.cashApi.state()); } catch { this.cash.set(null); } finally { this.loadingCash.set(false); }
  }
}
