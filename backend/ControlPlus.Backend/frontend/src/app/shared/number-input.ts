import { Directive, ElementRef, forwardRef, HostListener, inject, Input, OnChanges } from '@angular/core';
import { AbstractControl, ControlValueAccessor, NG_VALIDATORS, NG_VALUE_ACCESSOR, ValidationErrors, Validator } from '@angular/forms';

const formatter = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 20 });
export const groupedNumber = (value: number | null | undefined): string =>
  value == null || !Number.isFinite(value) ? '' : formatter.format(value);

/** Displays Colombian separators while forms and API contracts continue using numbers. */
@Directive({
  selector: 'input[cpNumberInput]',
  host: { type: 'text', inputmode: 'decimal', title: 'Miles con punto y decimales con coma. Ejemplo: 1.234,50' },
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => NumberInput), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => NumberInput), multi: true },
  ],
})
export class NumberInput implements ControlValueAccessor, Validator, OnChanges {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef);
  @Input() min: number | string | null = null;
  @Input() max: number | string | null = null;
  @Input() step: number | string | null = null;
  /** Decimal costs keep their canonical string until the existing API conversion. */
  @Input() cpNumberString = false;
  private onChange: (value: number | string | null) => void = () => {};
  private onTouched: () => void = () => {};
  private validatorChange = () => {};
  private lastText = '';
  private value: number | string | null = null;

  writeValue(value: number | string | null | undefined): void {
    this.value = value == null || value === '' ? null : this.cpNumberString ? String(value) : Number(value);
    this.render(this.displayValue());
  }
  registerOnChange(fn: (value: number | string | null) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  registerOnValidatorChange(fn: () => void): void { this.validatorChange = fn; }
  setDisabledState(disabled: boolean): void { this.element.nativeElement.disabled = disabled; }
  ngOnChanges(): void { this.validatorChange(); }

  @HostListener('input', ['$event']) input(event: InputEvent): void {
    const input = this.element.nativeElement;
    const raw = input.value;
    const caret = input.selectionStart ?? raw.length;
    const significantBefore = raw.slice(0, caret).replace(/\./g, '').length;
    // Never reinterpret pasted letters, scientific notation or an ambiguous dot decimal as money.
    const paste = event.inputType === 'insertFromPaste';
    if (!/^-?[\d.]*(?:,\d*)?$/.test(raw) || (paste && raw.includes('.') &&
      !/^-?\d{1,3}(?:\.\d{3})+(?:,\d*)?$/.test(raw))) {
      this.render(this.lastText); return;
    }
    const normalized = raw.replace(/\./g, '');
    const [integer, decimals] = normalized.split(',');
    const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    this.render(grouped + (decimals === undefined ? '' : ',' + decimals));
    const parsed = normalized === '' || normalized === '-' || normalized === ',' || normalized === '-,'
      ? null : this.cpNumberString ? normalized.replace(',', '.') : Number(normalized.replace(',', '.'));
    this.value = parsed;
    this.onChange(parsed);
    // Keep the logical position when a separator is inserted or removed before the caret.
    let position = 0, seen = 0;
    while (position < input.value.length && seen < significantBefore) {
      if (input.value[position] !== '.') seen++;
      position++;
    }
    input.setSelectionRange(position, position);
  }

  @HostListener('blur') blur(): void { this.render(this.displayValue()); this.onTouched(); }

  @HostListener('keydown', ['$event']) keydown(event: KeyboardEvent): void {
    if (this.cpNumberString || !['ArrowUp', 'ArrowDown'].includes(event.key) || this.element.nativeElement.readOnly || this.element.nativeElement.disabled) return;
    event.preventDefault();
    const step = this.step === 'any' ? 1 : Number(this.step ?? 1);
    if (!Number.isFinite(step) || step <= 0) return;
    let value = Number((Number(this.value ?? 0) + (event.key === 'ArrowUp' ? step : -step)).toFixed(12));
    if (this.min !== null && this.min !== '') value = Math.max(Number(this.min), value);
    if (this.max !== null && this.max !== '') value = Math.min(Number(this.max), value);
    this.value = value; this.render(groupedNumber(value)); this.onChange(value);
  }

  validate(control: AbstractControl): ValidationErrors | null {
    if (control.value == null || control.value === '') return null;
    // Un número escrito como texto por la pantalla («7000») también es válido; letras o espacios no.
    const value = this.cpNumberString || (typeof control.value === 'string' && /^-?\d+(?:\.\d+)?$/.test(control.value))
      ? Number(control.value) : control.value;
    if (typeof value !== 'number' || !Number.isFinite(value)) return { number: true };
    if (this.min !== null && this.min !== '' && value < Number(this.min)) return { min: { min: Number(this.min), actual: value } };
    if (this.max !== null && this.max !== '' && value > Number(this.max)) return { max: { max: Number(this.max), actual: value } };
    if (!this.cpNumberString && this.step !== null && this.step !== 'any' && Number(this.step) > 0) {
      const units = (value - Number(this.min ?? 0)) / Number(this.step);
      if (Math.abs(units - Math.round(units)) > 1e-7) return { step: true };
    }
    return null;
  }

  private displayValue(): string {
    if (!this.cpNumberString) return groupedNumber(this.value == null ? null : Number(this.value));
    if (this.value == null) return '';
    const [integer, decimals] = String(this.value).split('.');
    return integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.') + (decimals === undefined ? '' : ',' + decimals);
  }
  private render(text: string): void { this.lastText = text; this.element.nativeElement.value = text; }
}
