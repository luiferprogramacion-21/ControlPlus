/**
 * Calculadora de precio de venta (decisión del 18 de noviembre de 2026).
 * La ganancia se calcula sobre el costo: precio = costo × (1 + ganancia / 100), redondeado hacia arriba
 * de 50 en 50 pesos. Solo sugiere un valor; quien la usa puede cambiarlo.
 */
export const PRICE_ROUNDING_STEP = 50;

/** Precio sugerido para un costo y una ganancia en porcentaje; nulo si alguno no es válido. */
export function suggestedPrice(cost: number | null | undefined, profitPercent: number | null | undefined, step = PRICE_ROUNDING_STEP): number | null {
  if (cost == null || profitPercent == null) return null;
  const unitCost = Number(cost); const percent = Number(profitPercent);
  if (!Number.isFinite(unitCost) || unitCost <= 0 || !Number.isFinite(percent) || percent < 0) return null;
  const raw = unitCost * (1 + percent / 100);
  const price = Math.ceil(raw / step - 1e-9) * step;
  return Number.isSafeInteger(price) ? price : null;
}

/** Ganancia sobre el costo (en %, con un decimal) que deja un precio; nulo si no se puede calcular. */
export function profitPercentOf(price: number | null | undefined, cost: number | null | undefined): number | null {
  if (price == null || cost == null) return null;
  const sale = Number(price); const unitCost = Number(cost);
  if (!Number.isFinite(sale) || !Number.isFinite(unitCost) || unitCost <= 0 || sale < 0) return null;
  return Math.round(((sale - unitCost) / unitCost) * 1000) / 10;
}
