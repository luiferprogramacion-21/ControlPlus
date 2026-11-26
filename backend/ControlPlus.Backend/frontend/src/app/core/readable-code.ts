/**
 * Turns an internal code into words a shop employee understands (20 November 2026): «AJUSTE_ENTRADA» → «Ajuste entrada»,
 * «ANULACION_VENTA» → «Anulación venta». Screens never show codes in capitals with underscores.
 */
const ACCENTS: Record<string, string> = {
  anulacion: 'anulación', devolucion: 'devolución', liberacion: 'liberación', recepcion: 'recepción',
  credito: 'crédito', reimpresion: 'reimpresión', operacion: 'operación', correccion: 'corrección',
  apertura: 'apertura', retencion: 'retención', informacion: 'información', autorizacion: 'autorización',
};

/** Códigos en inglés que el servidor guarda en la auditoría (pruebas del usuario del 26/11/2026). */
const CODES: Record<string, string> = {
  SELF: 'Sin segundo autorizador', ADMINISTRATOR_SELF: 'El Administrador, sin segundo autorizador',
  SUPERVISOR_SELF: 'El Supervisor, sin segundo autorizador', PASSWORD: 'Usuario y contraseña', CARNE: 'Carné',
  CONTRASENA: 'Contraseña', CODIGO_BARRAS_PIN: 'Carné', PURCHASE_RECEIPT: 'Recepción de compra', SUCCESS: 'Exitoso',
  INVALID_AUTHORIZATION: 'Autorización no válida',
};

export function readableCode(code: string | null | undefined): string {
  if (!code) return '';
  if (CODES[code]) return CODES[code];
  if (!/^[A-Z0-9_]+$/.test(code)) return code;
  const words = code.toLowerCase().split('_').filter(Boolean).map(word => ACCENTS[word] ?? word);
  const text = words.join(' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}
