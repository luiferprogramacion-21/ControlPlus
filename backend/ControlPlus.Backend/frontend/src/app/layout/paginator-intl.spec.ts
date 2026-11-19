import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { SessionService } from '../core/session.service';
import { EstablishmentService } from '../core/establishment';
import { Shell } from './shell';
import { SpanishPaginatorIntl } from './paginator-intl';

describe('Paginador en español', () => {
  it('separa también los miles del rango y del total', () => {
    const intl = new SpanishPaginatorIntl();
    expect(intl.getRangeLabel(50, 20, 12345)).toBe('1.001–1.020 de 12.345');
    expect(intl.getRangeLabel(0, 0, 12345)).toBe('0 de 12.345');
    expect(intl.getRangeLabel(1000, 20, 12345)).toBe('12.345 de 12.345');
  });
  it('el menú lo entrega a las pantallas que contiene (no se registra en el paquete inicial)', async () => {
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), { provide: SessionService, useValue: { has: () => false, user: () => null, logout: () => Promise.resolve() } }, { provide: EstablishmentService, useValue: { name: () => 'Tienda', load: () => Promise.resolve() } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Shell);
    const intl = fixture.debugElement.injector.get(MatPaginatorIntl);
    expect(intl).toBeInstanceOf(SpanishPaginatorIntl);
    expect(intl.itemsPerPageLabel).toBe('Filas por página:');
    expect(intl.getRangeLabel(1, 20, 45)).toBe('21–40 de 45');
  });
});
