/** Manual discounts of a sale. The server recalculates and enforces the same rules (role limit, no wholesale, never below cost). */
export interface Discount { type: 'PORCENTAJE' | 'VALOR_FIJO'; value: number; }

/** The wholesale price only applies from this many units of the same product in one sale (the server enforces it too). */
export const WHOLESALE_MIN_QUANTITY = 12;

/** Maximum discount percentage by role. */
/** Descuento máximo por rol; decisión D7 del 23/11/2026: lo configura el Administrador (el suyo es 100 %, lo frena el costo). */
export interface DiscountLimits { cashier: number; supervisor: number; administrator: number; }
export const DEFAULT_DISCOUNT_LIMITS: DiscountLimits = { cashier: 5, supervisor: 20, administrator: 100 };
export function maxDiscountPercent(isAdmin: boolean, isSupervisor: boolean, limits: DiscountLimits = DEFAULT_DISCOUNT_LIMITS): number {
  return isAdmin ? limits.administrator : isSupervisor ? limits.supervisor : limits.cashier;
}

/** Amount taken off `gross` (COP, whole pesos). A fixed value can never exceed the gross amount. */
export function discountAmount(gross: number, discount: Discount | null): number {
  if (!discount || !(discount.value > 0) || !(gross > 0)) return 0;
  return discount.type === 'PORCENTAJE' ? Math.round(gross * discount.value / 100) : Math.min(Math.trunc(discount.value), gross);
}

/** Largest fixed amount the role may take off `gross`. */
export function maxDiscountAmount(gross: number, maxPercent: number): number { return Math.floor(gross * maxPercent / 100); }

const cop = (value: number): string => `$ ${Math.round(value).toLocaleString('es-CO')}`;

/** Returns the first problem with the discount, or an empty string when it is valid. */
export function validateDiscount(discount: Discount, gross: number, maxPercent: number): string {
  const value = Number(discount.value);
  if (!Number.isFinite(value) || value <= 0) return 'Escribe un descuento mayor que cero.';
  if (discount.type === 'PORCENTAJE') {
    if (value > maxPercent) return `Tu rol permite un descuento de hasta ${maxPercent} %.`;
    if (Math.round(value * 100) !== value * 100) return 'El porcentaje admite hasta dos decimales.';
    return '';
  }
  if (!Number.isSafeInteger(value)) return 'El valor del descuento debe ser un número entero de pesos.';
  if (value > gross) return `El descuento no puede superar el valor de la venta (${cop(gross)}).`;
  const limit = maxDiscountAmount(gross, maxPercent);
  if (value > limit) return `Tu rol permite un descuento de hasta ${maxPercent} %: máximo ${cop(limit)} sobre ${cop(gross)}.`;
  return '';
}

/** Short text for the screen: «10 %» or «$ 5.000». */
export function describeDiscount(discount: Discount): string {
  return discount.type === 'PORCENTAJE' ? `${discount.value.toLocaleString('es-CO')} %` : cop(discount.value);
}
