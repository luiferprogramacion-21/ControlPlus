import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormsModule, FormControl, ReactiveFormsModule } from '@angular/forms';
import { NumberInput } from './number-input';

@Component({ imports: [NumberInput, FormsModule, ReactiveFormsModule], template: `
  <input cpNumberInput [(ngModel)]="amount" min="0" step="0.0001">
  <input cpNumberInput [formControl]="quantity" min="1" max="10000" step="1">
  <input cpNumberInput [cpNumberString]="true" [(ngModel)]="cost" min="0">` })
class Host { amount: number | null = 1234567.5; quantity = new FormControl<number | null>(2500); cost: string | null = '1234.5000'; }

describe('Números con separadores colombianos', () => {
  async function setup() {
    const fixture = TestBed.createComponent(Host); fixture.detectChanges(); await fixture.whenStable();
    return { fixture, inputs: Array.from(fixture.nativeElement.querySelectorAll('input')) as HTMLInputElement[] };
  }
  async function enter(input: HTMLInputElement, text: string, paste = false) {
    input.value = text; input.setSelectionRange(text.length, text.length);
    input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: paste ? 'insertFromPaste' : 'insertText' }));
    await Promise.resolve();
  }
  it('muestra miles con punto y decimales con coma en ambos tipos de formulario', async () => {
    const { fixture, inputs } = await setup();
    expect(inputs.map(input => input.value)).toEqual(['1.234.567,5', '2.500', '1.234,5000']);
    await enter(inputs[0], '2.500.000,1250');
    expect(inputs[0].value).toBe('2.500.000,1250'); expect(fixture.componentInstance.amount).toBe(2500000.125);
    expect(typeof fixture.componentInstance.amount).toBe('number');
    await enter(inputs[1], '10000'); expect(inputs[1].value).toBe('10.000');
    expect(fixture.componentInstance.quantity.value).toBe(10000);
  });
  it('admite borrar todo, negativos y decimales sin convertir un campo vacío en cero', async () => {
    const { fixture, inputs } = await setup();
    await enter(inputs[0], ''); expect(fixture.componentInstance.amount).toBeNull();
    await enter(inputs[0], '-1234,25'); expect(inputs[0].value).toBe('-1.234,25');
    expect(fixture.componentInstance.amount).toBe(-1234.25);
    await enter(inputs[0], '1,'); expect(inputs[0].value).toBe('1,');
    await enter(inputs[0], '1,05'); expect(fixture.componentInstance.amount).toBe(1.05);
  });
  it('pega cifras colombianas y rechaza texto o puntos decimales ambiguos sin cambiar el importe', async () => {
    const { fixture, inputs } = await setup();
    await enter(inputs[0], '9.876,5432', true); expect(fixture.componentInstance.amount).toBe(9876.5432);
    await enter(inputs[0], '1234.50', true); expect(fixture.componentInstance.amount).toBe(9876.5432);
    await enter(inputs[0], '1e3'); expect(fixture.componentInstance.amount).toBe(9876.5432);
    expect(inputs[0].value).toBe('9.876,5432');
  });
  it('conserva límites, enteros, pasos, deshabilitado y teclado de cantidades', async () => {
    const { fixture, inputs } = await setup(); const control = fixture.componentInstance.quantity;
    await enter(inputs[1], '10.001'); expect(control.hasError('max')).toBe(true);
    await enter(inputs[1], '1,5'); expect(control.hasError('step')).toBe(true);
    await enter(inputs[1], '1'); inputs[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
    expect(control.value).toBe(2); expect(control.valid).toBe(true);
    control.disable(); fixture.detectChanges(); expect(inputs[1].disabled).toBe(true);
  });
  it('acepta un número guardado como texto (precio mayorista al editar) y rechaza texto que no es número', async () => {
    const { fixture } = await setup(); const control = fixture.componentInstance.quantity;
    control.setValue('7000' as unknown as number); expect(control.valid).toBe(true);
    control.setValue('7000.5' as unknown as number); expect(control.hasError('step')).toBe(true);
    control.setValue('siete' as unknown as number); expect(control.hasError('number')).toBe(true);
    control.setValue(' ' as unknown as number); expect(control.hasError('number')).toBe(true);
  });
  it('conserva todos los dígitos y cuatro decimales del costo antes de la conversión de la API', async () => {
    const { fixture, inputs } = await setup();
    await enter(inputs[2], '99.999.999.999.999,9999', true);
    expect(fixture.componentInstance.cost).toBe('99999999999999.9999');
    inputs[2].dispatchEvent(new Event('blur'));
    expect(inputs[2].value).toBe('99.999.999.999.999,9999');
    await enter(inputs[2], ''); expect(fixture.componentInstance.cost).toBeNull();
  });
});
