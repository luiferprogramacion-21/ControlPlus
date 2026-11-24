import { Component, computed, input, model, output, signal } from '@angular/core';

/** Una opción que se puede elegir: `value` es lo que usa la pantalla y `text` lo que se lee. */
export interface SuggestOption { value: string; text: string; hint?: string; }

/** «Crédito» y «credito» se buscan igual: sin tildes ni mayúsculas. */
export const foldText = (text: string): string =>
  text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLocaleLowerCase('es').trim();

/** Opciones que contienen todas las palabras escritas, en cualquier orden y sin importar tildes. */
export function suggestMatches(options: SuggestOption[], text: string, limit = 50): SuggestOption[] {
  const words = foldText(text).split(/\s+/).filter(Boolean);
  const found = words.length
    ? options.filter(option => { const haystack = foldText(`${option.text} ${option.hint ?? ''}`); return words.every(word => haystack.includes(word)); })
    : options;
  return found.slice(0, limit);
}

let sequence = 0;

/**
 * Campo de texto con sugerencias mientras se escribe (pruebas del usuario del 24/11/2026, punto 15). La lista se dibuja
 * dentro de la página, debajo del campo, para que siempre se vea con el tamaño de pantalla elegido. Flechas para moverse,
 * Enter o clic para elegir y Esc para cerrarla. Borrar el texto vuelve a «todos».
 */
@Component({
  selector: 'cp-suggest-field',
  template: `
    <input class="text-field suggest__input" type="text" [attr.name]="name() || null" [attr.aria-label]="label()" [placeholder]="placeholder()"
      autocomplete="off" role="combobox" aria-autocomplete="list" [attr.aria-expanded]="showList()" [attr.aria-controls]="listId"
      [attr.aria-activedescendant]="showList() && active() >= 0 ? listId + '-' + active() : null"
      [value]="text()" (input)="typed($any($event.target).value)" (focus)="open.set(true)" (blur)="open.set(false)" (keydown)="key($event)">
    @if (showList()) {
      <ul class="suggest__list" role="listbox" [id]="listId" [attr.aria-label]="label()">
        @for (option of matches(); track option.value; let i = $index) {
          <li role="option" [id]="listId + '-' + i" [class.is-active]="i === active()" [attr.aria-selected]="i === active()"
            (mousedown)="$event.preventDefault(); pick(option)" (mouseenter)="active.set(i)">
            <span>{{ option.text }}</span>@if (option.hint) { <small>{{ option.hint }}</small> }
          </li>
        }
      </ul>
    } @else if (open() && text().trim()) { <p class="suggest__none" role="status">No hay coincidencias: revisa lo escrito.</p> }`,
  host: { class: 'suggest' },
  styles: [`
    :host { position: relative; display: inline-block; min-width: 240px; }
    .suggest__input { width: 100%; }
    .suggest__list { position: absolute; top: calc(100% + 4px); left: 0; z-index: 30; min-width: 100%; max-width: 420px; max-height: 300px; overflow-y: auto;
      margin: 0; padding: 4px 0; list-style: none; border: 1px solid var(--cp-border); border-radius: 10px; background: var(--cp-surface);
      box-shadow: 0 8px 24px rgb(36 49 66 / 16%); }
    .suggest__list li { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; padding: 8px 12px; cursor: pointer; }
    .suggest__list li.is-active { background: var(--cp-primary-soft); }
    .suggest__list small { color: var(--cp-text-muted); font-size: 12px; white-space: nowrap; }
    .suggest__none { position: absolute; top: calc(100% + 4px); left: 0; z-index: 30; margin: 0; padding: 8px 12px; border: 1px solid var(--cp-border);
      border-radius: 10px; background: var(--cp-surface); color: var(--cp-text-muted); font-size: 13px; white-space: nowrap; }
  `],
})
export class SuggestField {
  readonly options = input.required<SuggestOption[]>();
  readonly placeholder = input('');
  readonly label = input('');
  readonly name = input('');
  readonly text = model('');
  /** El valor elegido; vacío cuando se borra el texto (todos). */
  readonly picked = output<string>();
  readonly open = signal(false);
  readonly active = signal(-1);
  readonly listId = `cp-suggest-${++sequence}`;
  readonly matches = computed(() => suggestMatches(this.options(), this.text()));
  readonly showList = computed(() => this.open() && this.matches().length > 0);

  typed(value: string): void {
    const wasEmpty = !this.text().trim();
    this.text.set(value); this.open.set(true);
    this.active.set(value.trim() && this.matches().length ? 0 : -1);
    if (!value.trim() && !wasEmpty) this.picked.emit('');
  }

  key(event: KeyboardEvent): void {
    const count = this.matches().length;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault(); this.open.set(true);
      if (!count) return;
      const step = event.key === 'ArrowDown' ? 1 : -1;
      this.active.set(Math.min(count - 1, Math.max(0, this.active() + step)));
    } else if (event.key === 'Enter' && this.showList() && this.active() >= 0) {
      event.preventDefault(); this.pick(this.matches()[this.active()]);
    } else if (event.key === 'Escape' && this.open()) {
      event.stopPropagation(); this.open.set(false);
    }
  }

  pick(option: SuggestOption): void {
    this.text.set(option.text); this.open.set(false); this.active.set(-1);
    this.picked.emit(option.value);
  }
}
