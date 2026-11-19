import { TestBed } from '@angular/core/testing';
import { activeScope, findRefreshButton, findSearchField, WindowsShortcuts } from './windows-shortcuts';

describe('Atajos de Windows', () => {
  let root: HTMLElement;
  beforeEach(() => {
    // jsdom no calcula diseño: todo elemento se considera visible.
    vi.spyOn(HTMLElement.prototype, 'getClientRects').mockReturnValue([{}] as unknown as DOMRectList);
    root = document.createElement('div');
    document.body.appendChild(root);
  });
  afterEach(() => { root.remove(); vi.restoreAllMocks(); });

  function screen(html: string): void { root.innerHTML = `<main id="main-content">${html}</main>`; }
  const press = (key: string, init: KeyboardEventInit = {}) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    document.dispatchEvent(event);
    return event;
  };

  it('encuentra la búsqueda junto al botón «Buscar» y el botón para actualizar', () => {
    screen(`<button>Actualizar</button><form><input id="q"><button type="submit">Buscar</button></form>`);
    const { scope, isWindow } = activeScope(document);
    expect(isWindow).toBe(false);
    expect(findSearchField(scope!)?.id).toBe('q');
    expect(findRefreshButton(scope!)?.textContent).toBe('Actualizar');
  });

  it('con una ventana abierta busca dentro de ella y F5 no recarga', () => {
    screen(`<form><input id="q"><button>Buscar</button></form><div class="module-form-sheet"><input id="inside-search"></div>`);
    TestBed.inject(WindowsShortcuts).start();
    expect(findSearchField(activeScope(document).scope!)?.id).toBe('inside-search');
    expect(press('F5').defaultPrevented).toBe(true);
  });

  it('Ctrl+B lleva el cursor a la búsqueda y F5 pulsa «Actualizar»', () => {
    screen(`<button id="refresh">Actualizar</button><form><input id="q"><button>Buscar</button></form>`);
    let refreshed = 0;
    root.querySelector('#refresh')!.addEventListener('click', () => refreshed++);
    TestBed.inject(WindowsShortcuts).start();
    expect(press('b', { ctrlKey: true }).defaultPrevented).toBe(true);
    expect(document.activeElement?.id).toBe('q');
    expect(press('F5').defaultPrevented).toBe(true);
    expect(refreshed).toBe(1);
  });

  it('el doble clic en una fila usa su botón «Ver…» y nunca uno de borrar', () => {
    screen(`<table><tbody><tr><td id="cell">Ana</td><td><button id="delete">Eliminar</button><button id="open">Ver detalle</button></td></tr></tbody></table>`);
    const clicked: string[] = [];
    root.querySelectorAll('button').forEach(button => button.addEventListener('click', () => clicked.push(button.id)));
    TestBed.inject(WindowsShortcuts).start();
    root.querySelector('#cell')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    expect(clicked).toEqual(['open']);
  });
});
