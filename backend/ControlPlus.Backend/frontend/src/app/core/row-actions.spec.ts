import { actionCell, arrangeRowActions, rowActions } from './row-actions';

describe('Acciones de fila como en Windows', () => {
  function row(buttons: string): HTMLTableRowElement {
    const table = document.createElement('table');
    table.innerHTML = `<tbody><tr><td>Ana</td><td>${buttons}</td></tr></tbody>`;
    return table.querySelector('tr')!;
  }

  it('con dos acciones o menos no agrega «Más»', () => {
    const cell = actionCell(row('<button class="table-action">Editar</button><button class="table-action">Historial</button>'))!;
    expect(arrangeRowActions(cell, document)).toEqual([]);
    expect(cell.querySelector('.row-actions__more')).toBeNull();
  });

  it('deja dos a la vista y lleva el resto, y siempre las de borrar, a «Más ▾»', () => {
    const cell = actionCell(row('<button class="table-action">Editar</button><button class="table-action">Eliminar</button><button class="table-action">Rol</button><button class="table-action">Carné</button>'))!;
    const hidden = arrangeRowActions(cell, document).map(button => button.textContent);
    expect(hidden).toEqual(['Eliminar', 'Carné']);
    expect(cell.querySelector('.row-actions__more')?.textContent).toBe('Más ▾');
    expect(rowActions(cell).length).toBe(4);
  });

  it('quita «Más» cuando sobran menos acciones', () => {
    const cell = actionCell(row('<button class="table-action">A</button><button class="table-action">B</button><button class="table-action">C</button>'))!;
    arrangeRowActions(cell, document);
    cell.querySelectorAll('button')[2].remove();
    arrangeRowActions(cell, document);
    expect(cell.querySelector('.row-actions__more')).toBeNull();
  });

  it('ignora filas sin botones de acción', () => {
    expect(actionCell(row('<span>—</span>'))).toBeNull();
  });
});
