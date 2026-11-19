import { groupedNumber } from '../shared/number-input';
import { Injectable, Provider } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

@Injectable()
export class SpanishPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Filas por página:';
  override nextPageLabel = 'Página siguiente';
  override previousPageLabel = 'Página anterior';
  override firstPageLabel = 'Primera página';
  override lastPageLabel = 'Última página';

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) return `0 de ${groupedNumber(length)}`;
    const total = Math.max(length, 0);
    const start = page * pageSize;
    if (start >= total) return `${groupedNumber(total)} de ${groupedNumber(total)}`;
    const end = Math.min(start + pageSize, total);
    return `${groupedNumber(start + 1)}–${groupedNumber(end)} de ${groupedNumber(total)}`;
  };
}

/** Paginador en español para las pantallas dentro del menú (las que muestran listas paginadas). */
export const paginatorIntlProvider: Provider = { provide: MatPaginatorIntl, useClass: SpanishPaginatorIntl };
