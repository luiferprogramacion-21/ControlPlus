import { DOCUMENT } from '@angular/common';
import { inject, Injectable, NgZone, OnDestroy } from '@angular/core';

/**
 * Acciones de fila como en Windows (decisión del 19 de noviembre de 2026):
 * - En cada fila de una lista se ven como máximo dos botones; los demás pasan a «Más ▾».
 * - El clic derecho sobre la fila abre un menú con todas sus acciones (menú contextual).
 * Los botones originales de cada pantalla no se reemplazan: el menú los pulsa, así se conservan sus permisos,
 * confirmaciones y estados (un botón deshabilitado aparece deshabilitado en el menú).
 */
export const VISIBLE_ROW_ACTIONS = 2;
const ACTION_SELECTOR = ':scope > button, :scope > a';
const HIDDEN_CLASS = 'row-action--hidden';
const MORE_CLASS = 'row-actions__more';

function label(element: Element): string { return (element.textContent ?? '').replace(/\s+/g, ' ').trim(); }
function isDisabled(element: HTMLElement): boolean { return (element as HTMLButtonElement).disabled || element.getAttribute('aria-disabled') === 'true'; }
function isDanger(element: HTMLElement): boolean { return element.classList.contains('table-action--danger') || element.classList.contains('button--danger') || /^(Eliminar|Borrar|Anular|Inactivar)\b/.test(label(element)); }

/** Celda de acciones de una fila: la última celda con botones de acción. */
export function actionCell(row: Element): HTMLElement | null {
  const cell = row.querySelector<HTMLElement>(':scope > td:last-child');
  return cell && cell.querySelector(':scope > .table-action, :scope > button.button, :scope > .mat-mdc-button-base') ? cell : null;
}

/** Botones de la acción de la fila, sin el «Más» que agrega este servicio. */
export function rowActions(cell: HTMLElement): HTMLElement[] {
  return Array.from(cell.querySelectorAll<HTMLElement>(ACTION_SELECTOR)).filter(element => !element.classList.contains(MORE_CLASS));
}

/** Deja a la vista las dos primeras acciones (las de borrar o anular siempre van al menú) y devuelve las ocultas. */
export function arrangeRowActions(cell: HTMLElement, doc: Document): HTMLElement[] {
  const actions = rowActions(cell);
  const hidden: HTMLElement[] = [];
  let shown = 0;
  for (const action of actions) {
    const keep = actions.length <= VISIBLE_ROW_ACTIONS || (!isDanger(action) && shown < VISIBLE_ROW_ACTIONS);
    action.classList.toggle(HIDDEN_CLASS, !keep);
    if (keep) shown++; else hidden.push(action);
  }
  let more = cell.querySelector<HTMLButtonElement>(`:scope > .${MORE_CLASS}`);
  if (hidden.length && !more) {
    more = doc.createElement('button');
    more.type = 'button';
    more.className = `table-action ${MORE_CLASS}`;
    more.textContent = 'Más ▾';
    more.setAttribute('aria-haspopup', 'menu');
    more.title = 'Más acciones (o clic derecho en la fila)';
    cell.appendChild(more);
  }
  if (!hidden.length && more) more.remove();
  return hidden;
}

@Injectable({ providedIn: 'root' })
export class RowActions implements OnDestroy {
  private readonly doc = inject(DOCUMENT);
  private readonly zone = inject(NgZone);
  private observer: MutationObserver | null = null;
  private menu: HTMLElement | null = null;
  private returnFocus: HTMLElement | null = null;
  private scheduled = false;

  private readonly onClick = (event: MouseEvent) => {
    const more = (event.target as HTMLElement | null)?.closest?.(`.${MORE_CLASS}`) as HTMLElement | null;
    if (more) {
      event.preventDefault();
      const cell = more.parentElement as HTMLElement;
      const rect = more.getBoundingClientRect();
      this.open(rowActions(cell).filter(action => action.classList.contains(HIDDEN_CLASS)), rect.left, rect.bottom, more);
      return;
    }
    if (this.menu && !this.menu.contains(event.target as Node)) this.close();
  };

  private readonly onContextMenu = (event: MouseEvent) => {
    const target = event.target as HTMLElement | null;
    if (!target || target.closest('input, textarea, select')) return;
    const row = target.closest('tbody tr');
    if (!row || !row.closest('#main-content, .module-form-sheet')) return;
    const cell = actionCell(row);
    if (!cell) return;
    const actions = rowActions(cell);
    if (!actions.length) return;
    event.preventDefault();
    this.open(actions, event.clientX, event.clientY, cell.querySelector<HTMLElement>(`.${MORE_CLASS}`) ?? actions[0]);
  };

  private readonly onKeydown = (event: KeyboardEvent) => {
    if (!this.menu) return;
    const items = Array.from(this.menu.querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));
    const index = items.indexOf(this.doc.activeElement as HTMLButtonElement);
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); this.close(true); }
    else if (event.key === 'ArrowDown') { event.preventDefault(); items[(index + 1) % items.length]?.focus(); }
    else if (event.key === 'ArrowUp') { event.preventDefault(); items[(index - 1 + items.length) % items.length]?.focus(); }
    else if (event.key === 'Tab') this.close();
  };

  start(): void {
    if (this.observer || typeof MutationObserver === 'undefined') return;
    this.zone.runOutsideAngular(() => {
      this.observer = new MutationObserver(() => this.schedule());
      this.observer.observe(this.doc.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
      this.doc.addEventListener('click', this.onClick, true);
      this.doc.addEventListener('contextmenu', this.onContextMenu);
      this.doc.addEventListener('keydown', this.onKeydown, true);
      this.doc.addEventListener('scroll', () => this.close(), true);
      this.schedule();
    });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.doc.removeEventListener('click', this.onClick, true);
    this.doc.removeEventListener('contextmenu', this.onContextMenu);
    this.doc.removeEventListener('keydown', this.onKeydown, true);
    this.close();
  }

  /** Reordena las filas visibles una vez por cuadro, aunque Angular cambie muchas cosas a la vez. */
  private schedule(): void {
    if (this.scheduled) return;
    this.scheduled = true;
    requestAnimationFrame(() => {
      this.scheduled = false;
      this.observer?.takeRecords();
      for (const row of Array.from(this.doc.querySelectorAll('#main-content tbody tr, .module-form-sheet tbody tr'))) {
        const cell = actionCell(row);
        if (cell) arrangeRowActions(cell, this.doc);
      }
      this.observer?.takeRecords();
    });
  }

  private open(actions: HTMLElement[], x: number, y: number, returnFocus: HTMLElement): void {
    this.close();
    if (!actions.length) return;
    const zoom = Number(getComputedStyle(this.doc.documentElement).getPropertyValue('--cp-zoom')) || 1;
    const menu = this.doc.createElement('div');
    menu.className = 'row-menu';
    menu.setAttribute('role', 'menu');
    menu.style.zoom = String(zoom);
    for (const action of actions) {
      const item = this.doc.createElement('button');
      item.type = 'button';
      item.setAttribute('role', 'menuitem');
      item.className = 'row-menu__item' + (isDanger(action) ? ' row-menu__item--danger' : '');
      item.textContent = label(action);
      item.disabled = isDisabled(action);
      item.addEventListener('click', () => { this.close(); this.zone.run(() => action.click()); });
      menu.appendChild(item);
    }
    this.doc.body.appendChild(menu);
    // Las coordenadas de pantalla se dividen entre el zoom propio del menú; si no cabe, se abre hacia arriba o la izquierda.
    const width = menu.offsetWidth * zoom, height = menu.offsetHeight * zoom;
    const left = Math.max(4, Math.min(x, innerWidth - width - 4));
    const top = y + height > innerHeight - 4 ? Math.max(4, y - height) : y;
    menu.style.left = `${left / zoom}px`;
    menu.style.top = `${top / zoom}px`;
    this.menu = menu;
    this.returnFocus = returnFocus;
    menu.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
  }

  private close(restoreFocus = false): void {
    if (!this.menu) return;
    this.menu.remove();
    this.menu = null;
    if (restoreFocus) this.returnFocus?.focus();
    this.returnFocus = null;
  }
}
