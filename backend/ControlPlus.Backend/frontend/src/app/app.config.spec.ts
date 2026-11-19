import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { appConfig } from './app.config';
import { NumberInput } from './shared/number-input';

@Component({
  imports: [FormsModule, NumberInput, CurrencyPipe, DecimalPipe],
  template: `
    <input cpNumberInput [(ngModel)]="amount">
    <output class="amount">{{ amount | currency:'COP':'symbol':'1.2-2' }}</output>
    <output class="quantity">{{ quantity | number:'1.0-0' }}</output>
    <output class="identifiers">{{ code }} · {{ phone }} · {{ document }}</output>`,
})
class NumericDisplay {
  amount = 1234567.5;
  quantity = 1234;
  code = 'CP00001234';
  phone = '3101234567';
  document = 'VTA-2026-1234';
}

describe('Formato numérico permanente de la aplicación', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [NumericDisplay], providers: appConfig.providers }));

  async function create(amount?: number) {
    const fixture = TestBed.createComponent(NumericDisplay);
    if (amount !== undefined) fixture.componentInstance.amount = amount;
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    return fixture;
  }

  it('usa la configuración de producción para mostrar miles y decimales también fuera de los campos', async () => {
    const fixture = await create();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('input')?.value).toBe('1.234.567,5');
    expect(root.querySelector('.amount')?.textContent).toContain('1.234.567,50');
    expect(root.querySelector('.quantity')?.textContent).toBe('1.234');
    expect(root.querySelector('.identifiers')?.textContent).toBe('CP00001234 · 3101234567 · VTA-2026-1234');
  });

  it('conserva el formato al editar, transportar el valor numérico y abrir otra vista', async () => {
    const fixture = await create();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input');
    input.value = '9.876.543,25';
    input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertFromPaste' }));
    fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.amount').textContent).toContain('9.876.543,25');
    const transported = JSON.parse(JSON.stringify({ amount: fixture.componentInstance.amount }));
    expect(transported.amount).toBe(9876543.25);
    fixture.destroy();
    const reopened = await create(transported.amount);
    expect(reopened.nativeElement.querySelector('input').value).toBe('9.876.543,25');
    expect(reopened.nativeElement.querySelector('.amount').textContent).toContain('9.876.543,25');
  });
});
