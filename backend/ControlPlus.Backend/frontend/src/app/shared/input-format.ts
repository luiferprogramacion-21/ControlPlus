import { Directive, ElementRef, HostListener, inject, input } from '@angular/core';
import { NgControl } from '@angular/forms';

/** Qué se puede escribir y hasta cuántos caracteres. */
export interface FormatRule { allowed: RegExp; max: number; numeric: boolean; }

/** Teléfono colombiano: solo números, hasta 10 (celular o fijo con indicativo). */
export const PHONE_RULE: FormatRule = { allowed: /\d/, max: 10, numeric: true };

/**
 * Documento según su tipo (auditoría del 23 de noviembre de 2026): CC, CE y PPT solo números (hasta 10); NIT números y
 * guion del dígito de verificación (hasta 12); pasaporte y otros, letras, números y guion (hasta 20).
 */
export function documentRule(type: string | null | undefined): FormatRule {
  switch ((type ?? '').trim().toUpperCase()) {
    case 'CC': case 'CE': case 'PPT': return { allowed: /\d/, max: 10, numeric: true };
    case 'NIT': return { allowed: /[\d-]/, max: 12, numeric: false };
    default: return { allowed: /[A-Za-z0-9-]/, max: 20, numeric: false };
  }
}

/** Quita lo que no se puede escribir y corta en el largo máximo. */
export function applyFormat(value: string, rule: FormatRule): string {
  return [...(value ?? '')].filter(char => rule.allowed.test(char)).join('').slice(0, rule.max);
}

/**
 * Campo de teléfono o documento que solo acepta los caracteres válidos y no deja pasar del largo real (pedido del usuario
 * en la auditoría: «que no deje digitar más de los que en realidad son, en todos los formularios»).
 * Uso: `<input cpFormat="phone">` o `<input cpFormat="document" [cpDocumentType]="tipo">`.
 */
@Directive({
  selector: 'input[cpFormat]',
  host: { '[attr.inputmode]': 'rule().numeric ? "numeric" : null', '[attr.maxlength]': 'rule().max', autocomplete: 'off' },
})
export class InputFormat {
  readonly cpFormat = input.required<'phone' | 'document'>();
  readonly cpDocumentType = input<string | null | undefined>(null);
  private readonly control = inject(NgControl, { optional: true, self: true });
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef);

  rule(): FormatRule { return this.cpFormat() === 'phone' ? PHONE_RULE : documentRule(this.cpDocumentType()); }

  @HostListener('input')
  onInput(): void {
    const element = this.element.nativeElement;
    const clean = applyFormat(element.value, this.rule());
    if (clean === element.value) return;
    element.value = clean;
    this.control?.control?.setValue(clean);
  }
}
