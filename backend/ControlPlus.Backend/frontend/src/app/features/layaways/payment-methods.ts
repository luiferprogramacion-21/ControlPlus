import { NumberInput } from '../../shared/number-input';
import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';

export type PaymentMethod = 'EFECTIVO' | 'TRANSFERENCIA' | 'NEQUI';
export type PaymentMode = 'payment' | 'refund';
export interface PaymentDraft { methodCode: PaymentMethod; appliedAmount: number; receivedAmount: number | null; reference: string; }

const METHODS: PaymentMethod[] = ['EFECTIVO', 'TRANSFERENCIA', 'NEQUI'];
const LIMIT = Number.MAX_SAFE_INTEGER;

export const money = (value: number): string => new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value);
export const emptyDraft = (): PaymentDraft => ({ methodCode: 'EFECTIVO', appliedAmount: 0, receivedAmount: null, reference: '' });
export const appliedTotal = (drafts: PaymentDraft[]): number => drafts.reduce((sum, draft) => sum + Number(draft.appliedAmount || 0), 0);
/** Only cash can produce change: received minus applied, never negative. */
export const cashChange = (drafts: PaymentDraft[]): number => {
  const cash = drafts.find(d => d.methodCode === 'EFECTIVO');
  return cash ? Math.max(0, Number(cash.receivedAmount || 0) - Number(cash.appliedAmount || 0)) : 0;
};

export interface DraftRules {
  mode: PaymentMode;
  /** The total must not exceed this value (for example the balance). */
  max?: number;
  overMessage?: string;
  /** The total must equal this value (for example a refund amount). */
  exact?: number;
  exactMessage?: string;
}

/** Returns the first problem found, or an empty string when the drafts are valid. */
export function validateDrafts(drafts: PaymentDraft[], rules: DraftRules): string {
  if (!drafts.length) return 'Agrega al menos un medio de pago.';
  if (new Set(drafts.map(d => d.methodCode)).size !== drafts.length) return 'Cada medio de pago se usa una sola vez.';
  if (drafts.some(d => !Number.isSafeInteger(Number(d.appliedAmount)) || d.appliedAmount <= 0 || d.appliedAmount > LIMIT)) return 'Cada importe debe ser un valor entero mayor que cero.';
  const total = appliedTotal(drafts);
  if (!Number.isSafeInteger(total) || (rules.max !== undefined && total > rules.max)) return rules.overMessage ?? 'El importe supera el máximo permitido.';
  if (rules.exact !== undefined && total !== rules.exact) return rules.exactMessage ?? 'Los importes deben sumar el valor indicado.';
  for (const d of drafts) {
    if (d.methodCode === 'EFECTIVO') {
      if (rules.mode === 'payment' && (!Number.isSafeInteger(Number(d.receivedAmount)) || Number(d.receivedAmount) < Number(d.appliedAmount) || Number(d.receivedAmount) > LIMIT)) return 'El efectivo recibido debe cubrir el importe aplicado.';
    // Propuesta 7 (21 de noviembre de 2026): en abonos la referencia es opcional; la devolución la sigue pidiendo.
    } else if (d.reference.trim().length > 200 || (rules.mode !== 'payment' && !d.reference.trim())) return rules.mode === 'payment' ? 'La referencia admite hasta 200 caracteres.' : 'Ingresa una referencia de hasta 200 caracteres para el pago electrónico.';
  }
  return '';
}

export const toPayments = (drafts: PaymentDraft[]) => drafts.map(d => ({
  methodCode: d.methodCode, appliedAmount: Number(d.appliedAmount),
  receivedAmount: d.methodCode === 'EFECTIVO' ? Number(d.receivedAmount) : null,
  reference: d.methodCode === 'EFECTIVO' ? null : d.reference.trim() || null,
}));
export const toRefunds = (drafts: PaymentDraft[]) => drafts.map(d => ({
  methodCode: d.methodCode, amount: Number(d.appliedAmount), reference: d.methodCode === 'EFECTIVO' ? null : d.reference.trim(),
}));

/** Editable list of up to three distinct payment methods; it edits the array it receives in place. */
@Component({
  selector: 'cp-payment-methods',
  imports: [NumberInput, FormsModule],
  template: `
    @for (draft of drafts; track $index; let i = $index) {
      <div class="payment-row">
        <label class="field-label" [for]="idPrefix + '-method-' + i">Medio de pago</label>
        <select class="text-field" [id]="idPrefix + '-method-' + i" [name]="idPrefix + '-method-' + i" [(ngModel)]="draft.methodCode">
          <option value="EFECTIVO">Efectivo</option><option value="TRANSFERENCIA">Transferencia</option><option value="NEQUI">Nequi</option>
        </select>
        <label class="field-label field-label--spaced" [for]="idPrefix + '-amount-' + i">{{ mode === 'refund' ? 'Valor a devolver' : 'Valor del abono' }}</label>
        <input class="text-field" [id]="idPrefix + '-amount-' + i" [name]="idPrefix + '-amount-' + i" type="text" cpNumberInput min="1" step="1" [(ngModel)]="draft.appliedAmount">
        @if (draft.methodCode === 'EFECTIVO') {
          @if (mode === 'payment') {
            <label class="field-label field-label--spaced" [for]="idPrefix + '-received-' + i">Efectivo recibido</label>
            <input class="text-field" [id]="idPrefix + '-received-' + i" [name]="idPrefix + '-received-' + i" type="text" cpNumberInput min="1" step="1" [(ngModel)]="draft.receivedAmount">
          }
        } @else {
          <label class="field-label field-label--spaced" [for]="idPrefix + '-reference-' + i">Referencia @if (mode === 'payment') { <span>(opcional)</span> }</label>
          <input class="text-field" [id]="idPrefix + '-reference-' + i" [name]="idPrefix + '-reference-' + i" maxlength="200" [(ngModel)]="draft.reference">
        }
        @if (drafts.length > 1) { <button class="table-action" type="button" (click)="remove(i)">Quitar medio</button> }
      </div>
    }
    @if (drafts.length < 3) { <button class="table-action" type="button" (click)="add()">Agregar medio</button> }`,
  styles: [`.payment-row { margin-bottom: 16px; padding-bottom: 16px; border-bottom: 1px solid var(--cp-border); } .payment-row .table-action { margin-top: 10px; }`],
})
export class PaymentMethods {
  @Input({ required: true }) drafts: PaymentDraft[] = [];
  @Input() mode: PaymentMode = 'payment';
  @Input() idPrefix = 'payment';

  add(): void {
    if (this.drafts.length >= 3) return;
    const next = METHODS.find(m => !this.drafts.some(d => d.methodCode === m));
    if (next) this.drafts.push({ methodCode: next, appliedAmount: 0, receivedAmount: null, reference: '' });
  }
  remove(index: number): void { if (this.drafts.length > 1) this.drafts.splice(index, 1); }
}
