/**
 * Fecha de calendario (aaaa-mm-dd) con el día de la semana, en palabras de tienda: «jueves, 8 de diciembre de 2026».
 * Se interpreta como fecha sin hora (UTC), para que no cambie de día por la zona horaria. Auditoría del 23/11/2026.
 */
export function dayDate(value: string | null | undefined): string {
  if (!value || !/^\d{4}-\d{2}-\d{2}/.test(value)) return value ?? '';
  const date = new Date(`${value.slice(0, 10)}T00:00:00Z`);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('es-CO', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(date);
}

/** Suma días a una fecha de calendario (aaaa-mm-dd) y devuelve otra fecha de calendario. */
export function addDays(value: string, days: number): string {
  const date = new Date(`${value.slice(0, 10)}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
