import { TestBed } from '@angular/core/testing';
import { SuggestField, SuggestOption, suggestMatches } from './suggest-field';

const options: SuggestOption[] = [
  { value: 'ana', text: 'Crédito de Ana', hint: 'Abono pendiente' },
  { value: 'luis', text: 'Venta de Luis', hint: 'Transferencia' },
  { value: 'luisa', text: 'Crédito de Luisa', hint: 'Nequi' },
];

describe('Campo de sugerencias', () => {
  function render() {
    const fixture = TestBed.createComponent(SuggestField);
    fixture.componentRef.setInput('options', options);
    fixture.componentRef.setInput('label', 'Buscar operación');
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    const picked: string[] = [];
    fixture.componentInstance.picked.subscribe(value => picked.push(value));
    return { fixture, input, picked };
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  it('busca palabras del nombre y la pista sin distinguir tildes, orden ni mayúsculas', () => {
    expect(suggestMatches(options, 'PENDIENTE credito')).toEqual([options[0]]);
    expect(suggestMatches(options, 'credito', 1)).toEqual([options[0]]);
    expect(suggestMatches(options, '  ', 2)).toEqual(options.slice(0, 2));
  });

  it('permite elegir con flechas y Enter e informa el valor elegido', () => {
    const { fixture, input, picked } = render();
    input.dispatchEvent(new Event('focus'));
    type(input, 'credito');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[role="option"]')).toHaveLength(2);
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(input.getAttribute('aria-activedescendant')).toBe(fixture.componentInstance.listId + '-0');

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(input.getAttribute('aria-activedescendant')).toBe(fixture.componentInstance.listId + '-1');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(picked).toEqual(['luisa']);
    expect(input.value).toBe('Crédito de Luisa');
    expect(input.getAttribute('aria-expanded')).toBe('false');
  });

  it('al borrar el texto vuelve a todas las opciones y avisa una sola vez', () => {
    const { fixture, input, picked } = render();
    input.dispatchEvent(new Event('focus'));
    type(input, 'Ana');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[role="option"]')).toHaveLength(1);

    type(input, '');
    fixture.detectChanges();
    expect(picked).toEqual(['']);
    expect(fixture.nativeElement.querySelectorAll('[role="option"]')).toHaveLength(options.length);
    type(input, '   ');
    expect(picked).toEqual(['']);
  });

  it('muestra la ausencia de coincidencias, Esc cierra y el ratón puede elegir', () => {
    const { fixture, input, picked } = render();
    input.dispatchEvent(new Event('focus'));
    type(input, 'inexistente');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain('No hay coincidencias');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
    expect(fixture.componentInstance.active()).toBe(-1);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();

    type(input, 'venta');
    fixture.detectChanges();
    const option = fixture.nativeElement.querySelector('[role="option"]') as HTMLElement;
    option.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(picked).toEqual(['luis']);
    expect(input.value).toBe('Venta de Luis');
    expect(fixture.nativeElement.querySelector('[role="listbox"]')).toBeNull();
  });
});
