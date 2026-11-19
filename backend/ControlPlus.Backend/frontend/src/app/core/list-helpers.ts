import { DOCUMENT } from '@angular/common';
import { inject, Injectable, NgZone, OnDestroy } from '@angular/core';

/**
 * Listas más simples (decisión del 19 de noviembre de 2026), en todas las pantallas a la vez:
 * - Los buscadores de las listas buscan mientras se escribe y los filtros se aplican al elegirlos: el formulario que
 *   tiene el botón «Buscar» o «Filtrar» se envía solo y ese botón deja de mostrarse (Enter sigue funcionando).
 * - Una lista vacía ofrece el botón para crear, el mismo «Nuevo…» de la pantalla.
 * Solo actúa sobre el contenido de la pantalla, no sobre las ventanas, donde buscar es un paso explícito.
 */
export const SEARCH_DELAY_MS = 400;
const SUBMIT_LABELS = new Set(['Buscar', 'Filtrar']);
const AUTO_CLASS = 'list-auto-search';

function label(element: Element): string { return (element.textContent ?? '').replace(/\s+/g, ' ').trim(); }

/** El formulario de búsqueda de una lista: dentro de la pantalla (no en una ventana) y con un botón Buscar/Filtrar. */
export function listSearchForm(element: Element | null): HTMLFormElement | null {
  const form = element?.closest('form');
  if (!form || !form.closest('#main-content') || form.closest('.module-form-sheet, [role="dialog"]')) return null;
  return Array.from(form.querySelectorAll('button')).some(button => SUBMIT_LABELS.has(label(button))) ? form : null;
}

/** El botón «Nuevo…» principal de la pantalla, para ofrecerlo cuando la lista está vacía. */
export function createButton(scope: ParentNode): HTMLElement | null {
  return Array.from(scope.querySelectorAll<HTMLElement>('button, a.button'))
    .find(button => /^(Nuevo|Nueva)\b/.test(label(button)) && !(button as HTMLButtonElement).disabled
      && !button.classList.contains('list-empty-create') && !button.closest('tbody, .module-form-sheet, [role="dialog"]')) ?? null;
}

@Injectable({ providedIn: 'root' })
export class ListHelpers implements OnDestroy {
  private readonly doc = inject(DOCUMENT);
  private readonly zone = inject(NgZone);
  private readonly timers = new Map<HTMLFormElement, ReturnType<typeof setTimeout>>();
  private observer: MutationObserver | null = null;
  private scheduled = false;

  private readonly onInput = (event: Event) => {
    const target = event.target as HTMLElement | null;
    if (!(target instanceof HTMLInputElement) || !['text', 'search', ''].includes(target.type)) return;
    const form = listSearchForm(target);
    if (form) this.submitLater(form, SEARCH_DELAY_MS);
  };

  private readonly onChange = (event: Event) => {
    const target = event.target as HTMLElement | null;
    if (!(target instanceof HTMLSelectElement) && !(target instanceof HTMLInputElement && ['checkbox', 'date'].includes(target.type))) return;
    const form = listSearchForm(target);
    if (form) this.submitLater(form, 0);
  };

  start(): void {
    if (this.observer || typeof MutationObserver === 'undefined') return;
    this.zone.runOutsideAngular(() => {
      this.doc.addEventListener('input', this.onInput, true);
      this.doc.addEventListener('change', this.onChange, true);
      this.observer = new MutationObserver(() => this.schedule());
      this.observer.observe(this.doc.body, { childList: true, subtree: true });
      this.schedule();
    });
  }

  ngOnDestroy(): void {
    this.doc.removeEventListener('input', this.onInput, true);
    this.doc.removeEventListener('change', this.onChange, true);
    this.observer?.disconnect();
    this.timers.forEach(timer => clearTimeout(timer));
  }

  private submitLater(form: HTMLFormElement, delay: number): void {
    clearTimeout(this.timers.get(form));
    this.timers.set(form, setTimeout(() => {
      this.timers.delete(form);
      if (form.isConnected) this.zone.run(() => form.requestSubmit());
    }, delay));
  }

  private schedule(): void {
    if (this.scheduled) return;
    this.scheduled = true;
    requestAnimationFrame(() => {
      this.scheduled = false;
      this.observer?.takeRecords();
      this.enhance();
      this.observer?.takeRecords();
    });
  }

  /** Marca los formularios de búsqueda automática y agrega el botón de crear a las listas vacías. */
  private enhance(): void {
    const main = this.doc.querySelector('#main-content');
    if (!main) return;
    for (const button of Array.from(main.querySelectorAll<HTMLButtonElement>('form button'))) {
      if (SUBMIT_LABELS.has(label(button)) && listSearchForm(button)) button.classList.add(AUTO_CLASS);
    }
    const create = createButton(main);
    for (const cell of Array.from(main.querySelectorAll<HTMLElement>('tbody tr.empty-row > td, tbody tr:only-child > td[colspan]'))) {
      if (cell.closest('.module-form-sheet, [role="dialog"], .cart-table')) continue;
      const existing = cell.querySelector<HTMLButtonElement>('.list-empty-create');
      if (!create) { existing?.remove(); continue; }
      if (existing) { existing.textContent = label(create); continue; }
      const button = this.doc.createElement('button');
      button.type = 'button';
      button.className = 'button button--primary list-empty-create';
      button.textContent = label(create);
      button.addEventListener('click', () => this.zone.run(() => (createButton(main) ?? create).click()));
      cell.appendChild(button);
    }
  }
}
