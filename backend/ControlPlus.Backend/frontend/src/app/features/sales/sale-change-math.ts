/** Pure rules of a post-sale product change. The server recalculates everything; these mirror it so the screen can prepare exact payments. */

/** Historical value of returning `returning` units: net per unit truncated, with the remainder on the last units. Mirrors SaleChangeAvailability. */
export function returnValue(netAmount: number, soldQuantity: number, previouslyReturned: number, returning: number): number {
  if (soldQuantity < 1 || returning < 1) return 0;
  const perItem = Math.trunc(netAmount / soldQuantity);
  const remainder = previouslyReturned + returning === soldQuantity ? netAmount - perItem * soldQuantity : 0;
  return perItem * returning + remainder;
}

export type ChangeTreatment = 'AUTO' | 'PAGO_INMEDIATO' | 'AGREGAR_CREDITO';
export type SettlementMode = 'none' | 'payment' | 'refund';
export interface Settlement {
  treatment: ChangeTreatment | '';
  mode: SettlementMode;
  /** Amount that must be covered by payments or refunds (zero when none). */
  cashAmount: number;
  /** Amount added to or taken from the credit balance. */
  creditAdjustment: number;
  /** A credit sale with a positive difference needs the operator to choose how to settle it. */
  needsChoice: boolean;
  error: string;
}

/** Decides how a difference is settled. `creditBalance` is null for cash sales. */
export function settle(saleType: string, difference: number, creditBalance: number | null, choice: ChangeTreatment | ''): Settlement {
  const base = { treatment: '' as const, mode: 'none' as SettlementMode, cashAmount: 0, creditAdjustment: 0, needsChoice: false, error: '' };
  if (difference === 0) return { ...base, treatment: 'AUTO' };
  if (difference > 0) {
    if (saleType !== 'CREDITO') return { ...base, treatment: 'AUTO', mode: 'payment', cashAmount: difference };
    if (choice === 'PAGO_INMEDIATO') return { ...base, treatment: 'PAGO_INMEDIATO', mode: 'payment', cashAmount: difference, needsChoice: true };
    if (choice === 'AGREGAR_CREDITO') return { ...base, treatment: 'AGREGAR_CREDITO', creditAdjustment: difference, needsChoice: true };
    return { ...base, needsChoice: true, error: 'Elige si la diferencia se cobra ahora o se suma al crédito.' };
  }
  const refund = -difference;
  if (saleType !== 'CREDITO' || creditBalance === null) return { ...base, treatment: 'AUTO', mode: 'refund', cashAmount: refund };
  if (creditBalance <= 0) return { ...base, error: 'El crédito no tiene saldo para reducir.' };
  const reduction = Math.min(refund, creditBalance);
  const cash = refund - reduction;
  return { ...base, treatment: 'AUTO', mode: cash > 0 ? 'refund' : 'none', cashAmount: cash, creditAdjustment: -reduction };
}
