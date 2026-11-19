import { DOCUMENT } from '@angular/common';
import { inject, Injectable, NgZone, OnDestroy } from '@angular/core';

/**
 * Atajos de Windows en todas las pantallas (decisión del 19 de noviembre de 2026):
 * - Ctrl+B (o Ctrl+F): lleva el cursor a la búsqueda de la pantalla o de la ventana abierta.
 * - F5: actualiza la lista con el botón «Actualizar» (o «Buscar») de la pantalla, sin recargar la página;
 *   con una ventana abierta no hace nada, para no perder lo escrito.
 * - Doble clic en una fila: la abre con su botón «Ver…», «Editar…», «Historial» o «Movimientos».
 * - Al pasar el ratón por «Buscar» y «Actualizar» se muestra su atajo.
 * Enter acepta y Esc cancela en los formularios y ventanas (ver shared/window-sheet.ts).
 */
const OPEN_ROW_ACTION = /^(Ver|Editar|Abrir|Movimientos|Historial)\b/;
const WINDOW_SELECTOR = '.module-form-sheet, .cdk-overlay-container .mat-mdc-dialog-container, [role="dialog"][aria-modal="true"]';

function label(element: Element): string { return (element.textContent ?? '').replace(/\s+/g, ' ').trim(); }
function visible(element: HTMLElement): boolean { return !!(element.offsetParent || element.getClientRects().length); }
function enabled(element: HTMLElement): boolean { return !(element as HTMLButtonElement).disabled; }

/** La ventana abierta más reciente (la de arriba) o, si no hay ninguna, el contenido de la pantalla. */
export function activeScope(doc: Document): { scope: HTMLElement | null; isWindow: boolean } {
  const windows = Array.from(doc.querySelectorAll<HTMLElement>(WINDOW_SELECTOR)).filter(visible);
  if (windows.length) return { scope: windows[windows.length - 1], isWindow: true };
  return { scope: doc.querySelector<HTMLElement>('#main-content'), isWindow: false };
}

export function findSearchField(scope: HTMLElement): HTMLInputElement | null {
  const usable = (input: HTMLInputElement | null) => input && visible(input) && enabled(input) ? input : null;
  const pos = usable(scope.querySelector<HTMLInputElement>('#product-search'));
  if (pos) return pos;
  for (const button of Array.from(scope.querySelectorAll<HTMLButtonElement>('button'))) {
    if (label(button) !== 'Buscar' || !visible(button)) continue;
    const container = button.closest('form') ?? button.parentElement;
    const input = usable(container?.querySelector<HTMLInputElement>('input:not([type]), input[type="text"], input[type="search"]') ?? null);
    if (input) return input;
  }
  for (const input of Array.from(scope.querySelectorAll<HTMLInputElement>('input[id*="search"], input[id*="filter"]'))) {
    if (usable(input)) return input;
  }
  return null;
}

export function findRefreshButton(scope: HTMLElement): HTMLButtonElement | null {
  const buttons = Array.from(scope.querySelectorAll<HTMLButtonElement>('button')).filter(button => visible(button) && enabled(button));
  return buttons.find(button => label(button) === 'Actualizar') ?? buttons.find(button => label(button) === 'Buscar') ?? null;
}

@Injectable({ providedIn: 'root' })
export class WindowsShortcuts implements OnDestroy {
  private readonly doc = inject(DOCUMENT);
  private readonly zone = inject(NgZone);
  private started = false;

  private readonly onKeydown = (event: KeyboardEvent) => {
    if (event.defaultPrevented || event.altKey) return;
    const key = event.key.toLowerCase();
    if ((event.ctrlKey || event.metaKey) && !event.shiftKey && (key === 'b' || key === 'f')) {
      const { scope } = activeScope(this.doc);
      const field = scope ? findSearchField(scope) : null;
      if (!field) return;
      event.preventDefault();
      field.focus();
      field.select?.();
      return;
    }
    if (event.key === 'F5' && !event.ctrlKey && !event.shiftKey && !event.metaKey) {
      const { scope, isWindow } = activeScope(this.doc);
      if (isWindow) { event.preventDefault(); return; }
      const button = scope ? findRefreshButton(scope) : null;
      if (!button) return;
      event.preventDefault();
      this.zone.run(() => button.click());
    }
  };

  private readonly onDoubleClick = (event: MouseEvent) => {
    const target = event.target as HTMLElement | null;
    if (!target || target.closest('button, a, input, select, textarea, label')) return;
    const row = target.closest('tbody tr');
    if (!row || !row.closest('#main-content, .module-form-sheet')) return;
    const action = Array.from(row.querySelectorAll<HTMLElement>('button, a')).find(element => OPEN_ROW_ACTION.test(label(element)) && enabled(element));
    if (!action) return;
    this.doc.getSelection?.()?.removeAllRanges();
    this.zone.run(() => action.click());
  };

  private readonly onPointerOver = (event: MouseEvent) => {
    const button = (event.target as HTMLElement | null)?.closest?.('button');
    if (!button || button.title) return;
    const text = label(button);
    if (text === 'Buscar') button.title = 'Buscar (Ctrl+B)';
    else if (text === 'Actualizar') button.title = 'Actualizar (F5)';
  };

  start(): void {
    if (this.started) return;
    this.started = true;
    this.zone.runOutsideAngular(() => {
      this.doc.addEventListener('keydown', this.onKeydown);
      this.doc.addEventListener('dblclick', this.onDoubleClick);
      this.doc.addEventListener('pointerover', this.onPointerOver);
    });
  }

  ngOnDestroy(): void {
    this.doc.removeEventListener('keydown', this.onKeydown);
    this.doc.removeEventListener('dblclick', this.onDoubleClick);
    this.doc.removeEventListener('pointerover', this.onPointerOver);
  }
}
