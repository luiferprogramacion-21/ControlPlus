import { CashFlowDay, CashFlowEntry } from './reports-api';

export const MAX_REPORT_DAYS = 31;
const ISO = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Parses YYYY-MM-DD as a real calendar date (UTC), or null. */
export function parseIsoDate(value: string): Date | null {
  const match = ISO.exec(value ?? '');
  if (!match) return null;
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day ? date : null;
}

export function formatIsoDate(date: Date): string { return date.toISOString().slice(0, 10); }

/** Last seven local days ending today. */
export function defaultRange(now = new Date()): { from: string; to: string } {
  const today = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
  return { from: formatIsoDate(new Date(today.getTime() - 6 * 86_400_000)), to: formatIsoDate(today) };
}

/** Inclusive number of days, or 0 when a date is invalid. */
export function rangeDays(from: string, to: string): number {
  const start = parseIsoDate(from), end = parseIsoDate(to);
  return start && end ? Math.round((end.getTime() - start.getTime()) / 86_400_000) + 1 : 0;
}

/** Client-side check before asking the server, which keeps validating on its own. Empty text means valid. */
export function validateRange(from: string, to: string): string {
  if (!from || !to) return 'Indica la fecha inicial y la final.';
  if (!parseIsoDate(from) || !parseIsoDate(to)) return 'Las fechas no son válidas.';
  const days = rangeDays(from, to);
  if (days < 1) return 'La fecha inicial no puede ser posterior a la final.';
  if (days > MAX_REPORT_DAYS) return `El rango admite hasta ${MAX_REPORT_DAYS} días.`;
  return '';
}

/** Heights in percent of the tallest bar; a zero or negative day is 0. */
export function barHeights(values: number[]): number[] {
  const max = Math.max(0, ...values);
  return values.map(value => (max > 0 && value > 0 ? Math.max(2, (value / max) * 100) : 0));
}

const ORIGINS: Record<string, string> = {
  VENTA_CONTADO: 'Venta de contado', ABONO_CREDITO: 'Abono de crédito', PAGO_APARTADO: 'Pago de apartado',
  COBRO_CAMBIO_VENTA: 'Cobro por cambio de venta', DEVOLUCION_CAMBIO_VENTA: 'Devolución por cambio de venta',
  REEMBOLSO_APARTADO: 'Reembolso de apartado', INGRESO: 'Ingreso manual', INGRESO_MANUAL: 'Ingreso manual',
  GASTO: 'Gasto', GASTO_MANUAL: 'Gasto', SANGRIA: 'Sangría', SANGRIA_MANUAL: 'Sangría', REVERSO: 'Reverso',
};
const sentence = (code: string): string => { const text = code.replace(/_/g, ' ').toLowerCase(); return text.charAt(0).toUpperCase() + text.slice(1); };
export const originLabel = (code: string): string => ORIGINS[code] ?? sentence(code);
export const methodLabel = (code: string | null): string => (code ? sentence(code) : '—');

export interface CashFlowRow { kind: 'Cobro' | 'Manual'; origin: string; method: string | null; total: number; }

/** Adds the daily entries of the range by origin and method, keeping the sign of each amount. */
export function aggregateCashFlow(days: CashFlowDay[]): CashFlowRow[] {
  const rows = new Map<string, CashFlowRow>();
  const add = (kind: CashFlowRow['kind'], entry: CashFlowEntry): void => {
    const key = `${kind}|${entry.origin}|${entry.method ?? ''}`;
    const row = rows.get(key) ?? { kind, origin: entry.origin, method: entry.method, total: 0 };
    row.total += entry.amount; rows.set(key, row);
  };
  for (const day of days) { day.payments.forEach(entry => add('Cobro', entry)); day.manualMovements.forEach(entry => add('Manual', entry)); }
  return [...rows.values()].sort((a, b) => a.kind.localeCompare(b.kind) || a.origin.localeCompare(b.origin) || (a.method ?? '').localeCompare(b.method ?? ''));
}
