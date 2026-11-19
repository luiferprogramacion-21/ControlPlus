import { createButton, listSearchForm, SEARCH_DELAY_MS, ListHelpers } from './list-helpers';
import { TestBed } from '@angular/core/testing';

describe('Listas más simples', () => {
  let root: HTMLElement;
  beforeEach(() => { root = document.createElement('div'); document.body.appendChild(root); });
  afterEach(() => { root.remove(); vi.useRealTimers(); });

  it('reconoce el buscador de una lista y no el de una ventana', () => {
    root.innerHTML = `<main id="main-content"><form id="list"><input id="q"><button>Buscar</button></form>
      <div class="module-form-sheet"><form><input id="w"><button>Buscar</button></form></div></main>`;
    expect(listSearchForm(root.querySelector('#q'))?.id).toBe('list');
    expect(listSearchForm(root.querySelector('#w'))).toBeNull();
  });

  it('ofrece el botón «Nuevo…» de la pantalla, no los de las filas', () => {
    root.innerHTML = `<main id="main-content"><table><tbody><tr><td><button>Nuevo abono</button></td></tr></tbody></table><button id="new">Nuevo cliente</button></main>`;
    expect(createButton(root)?.id).toBe('new');
  });

  it('busca sola mientras se escribe', () => {
    vi.useFakeTimers();
    root.innerHTML = `<main id="main-content"><form><input id="q"><button>Buscar</button></form></main>`;
    const form = root.querySelector('form')!;
    let submits = 0;
    form.addEventListener('submit', event => { event.preventDefault(); submits++; });
    TestBed.inject(ListHelpers).start();
    const input = root.querySelector<HTMLInputElement>('#q')!;
    input.value = 'an'; input.dispatchEvent(new Event('input', { bubbles: true }));
    input.value = 'ana'; input.dispatchEvent(new Event('input', { bubbles: true }));
    vi.advanceTimersByTime(SEARCH_DELAY_MS);
    expect(submits).toBe(1);
  });
});
