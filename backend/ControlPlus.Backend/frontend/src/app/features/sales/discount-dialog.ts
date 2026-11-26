import { NumberInput } from '../../shared/number-input';
import { A11yModule } from '@angular/cdk/a11y';
import { CurrencyPipe } from '@angular/common';
import { Component, input, OnInit, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeDiscount, Discount, discountAmount, maxDiscountAmount, validateDiscount } from './sale-discount';

export interface DiscountApplied { discount: Discount; reason: string; }

/** Floating window to apply a discount, by percentage or by value, to a product or to the whole sale. */
@Component({ selector: 'cp-discount-dialog', imports: [NumberInput, FormsModule, A11yModule, CurrencyPipe], templateUrl: './discount-dialog.html', styles: [`
  .discount-mode { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; padding: 4px; border-radius: 10px; background: var(--cp-table-head); }
  .discount-mode__option { min-height: 40px; border: 0; border-radius: 8px; background: transparent; color: var(--cp-text-secondary); font: inherit; font-weight: 600; cursor: pointer; }
  .discount-mode__option.is-active { background: var(--cp-surface); color: var(--cp-primary); box-shadow: 0 1px 3px rgb(22 34 47 / 18%); }
  .discount-hint { margin: 6px 0 0; color: var(--cp-text-muted); font-size: 12px; }
  .discount-preview { margin: 14px 0 0; padding: 10px 14px; border-radius: 10px; background: var(--cp-table-head); }
  .discount-preview div { display: flex; justify-content: space-between; gap: 12px; padding: 3px 0; }
  .discount-preview dt { color: var(--cp-text-secondary); } .discount-preview dd { margin: 0; }
`] })
export class DiscountDialog implements OnInit {
  readonly scope = input.required<'product' | 'sale'>();
  /** Product name for a line discount; the number of products for the whole sale. */
  readonly target = input('');
  /** Value the discount applies to (the line, or the sale subtotal). */
  readonly gross = input.required<number>();
  readonly maxPercent = input.required<number>();
  /** Lo más que se puede descontar sin quedar por debajo del costo; null si no se conoce el costo. */
  readonly costFloor = input<number | null>(null);
  readonly current = input<Discount | null>(null);
  readonly currentReason = input('');
  /** What the other kind of discount will lose when this one is applied. */
  readonly replaces = input('');
  readonly applied = output<DiscountApplied>();
  readonly removed = output<void>();
  readonly closed = output<void>();
  type: Discount['type'] = 'PORCENTAJE';
  value: number | null = null;
  reason = '';
  error = '';

  ngOnInit(): void {
    const current = this.current();
    if (current) { this.type = current.type; this.value = current.value; }
    this.reason = this.currentReason();
  }

  get title(): string { return this.scope() === 'product' ? 'Descuento del producto' : 'Descuento de la venta total'; }
  get discount(): Discount | null { return this.value === null || this.value === undefined || `${this.value}` === '' ? null : { type: this.type, value: Number(this.value) }; }
  /** Live preview of what the discount takes off and what is left to pay. */
  get amount(): number { return discountAmount(this.gross(), this.discount); }
  get limitText(): string {
    const format = (value: number) => new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(value);
    const roleMax = maxDiscountAmount(this.gross(), this.maxPercent());
    const floor = this.costFloor();
    if (floor !== null && floor < roleMax) return `Máximo ${format(floor)}: con más se vendería por debajo del costo.`;
    return `Máximo ${this.maxPercent()} % (${format(roleMax)}) según tu rol.`;
  }
  get currentText(): string { const c = this.current(); return c ? describeDiscount(c) : ''; }

  setType(type: Discount['type']): void { if (this.type !== type) { this.type = type; this.value = null; this.error = ''; } }

  apply(): void {
    const discount = this.discount;
    this.error = discount ? validateDiscount(discount, this.gross(), this.maxPercent()) : 'Escribe el valor del descuento.';
    const floor = this.costFloor();
    if (!this.error && discount && floor !== null && discountAmount(this.gross(), discount) > floor)
      this.error = `Con ese descuento se vendería por debajo del costo. Lo máximo que puedes descontar es ${new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(floor)}.`;
    if (this.error || !discount) return;
    this.applied.emit({ discount: { type: discount.type, value: discount.value }, reason: this.reason.trim() });
  }
}
