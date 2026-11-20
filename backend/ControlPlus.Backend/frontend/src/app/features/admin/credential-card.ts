import { code128Widths } from '../../core/code128';

export interface CredentialCard { businessName: string; displayName: string; userName: string; roleName: string; token: string; fragment: string; }

const escape = (value: string): string => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

/** The card is printed on the 80 mm roll of the receipt printer (72 mm printable). */
export const PRINTED_CARD_WIDTH_MM = 70;
/**
 * Narrowest bar: 0.5 mm = 4 dots at 203 dpi (20 November 2026, option A chosen by the user). At the store, product labels
 * read only when their bars had about 4 dots (an 80 × 30 mm label); with 2 or 3 dots the thermal head closes the spaces and
 * the laser scanner (Honeywell Voyager MS9520) does not even beep. At 4 dots the card code does not fit across the roll, so
 * it is printed lengthwise and the card is turned to scan it. Never scaled by the browser.
 */
export const PRINTED_MODULE_MM = 0.5;
/** Width of the bars across the roll: wide, so the scanner's line easily crosses all of them. */
const BAR_LENGTH_MM = 40;
/** Blank margin each side of the bars: at least 10 modules, as Code 128 requires. */
const QUIET_MODULES = 10;

/**
 * Code 128 SVG with a fixed size in millimetres (no scaling by the browser), printed lengthwise: the bars are horizontal and
 * follow one another down the roll, so the code can be as long as it needs at 4 dots per bar.
 */
export function printedBarcodeSvg(token: string, barLengthMm = BAR_LENGTH_MM, moduleMm = PRINTED_MODULE_MM): string {
  const widths = code128Widths(token);
  const modules = widths.reduce((sum, width) => sum + width, 0) + 2 * QUIET_MODULES;
  let y = QUIET_MODULES * moduleMm; let bars = '';
  widths.forEach((width, index) => {
    if (index % 2 === 0) bars += `<rect x="0" y="${y.toFixed(4)}" width="${barLengthMm}" height="${(width * moduleMm).toFixed(4)}"/>`;
    y += width * moduleMm;
  });
  const total = (modules * moduleMm).toFixed(4);
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${barLengthMm}mm" height="${total}mm" viewBox="0 0 ${barLengthMm} ${total}" role="img" aria-label="Código de barras del carné" shape-rendering="crispEdges" fill="#000">${bars}</svg>`;
}

/** Card for the 80 mm receipt printer with the barcode the reader scans, printed lengthwise. */
export function buildCredentialCardHtml(card: CredentialCard): string {
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Carné ${escape(card.userName)}</title>
<style>
  @page { size: 80mm auto; margin: 3mm 5mm; }
  * { box-sizing: border-box; }
  body { margin: 0; width: ${PRINTED_CARD_WIDTH_MM}mm; font-family: Arial, Helvetica, sans-serif; color: #000; }
  .card { width: ${PRINTED_CARD_WIDTH_MM}mm; padding: 3mm 0; display: flex; flex-direction: column; gap: 3mm; border: 0.3mm dashed #000; }
  header, .person { padding: 0 3mm; }
  header { display: flex; justify-content: space-between; align-items: baseline; gap: 2mm; }
  header strong { font-size: 12pt; }
  header span { font-size: 7pt; text-transform: uppercase; white-space: nowrap; }
  .person { margin: 0; }
  .person b { display: block; font-size: 11pt; }
  .person small { font-size: 8pt; }
  .barcode { display: flex; flex-direction: column; align-items: center; gap: 1mm; }
  .barcode svg { display: block; }
  .barcode small { display: block; font-size: 7pt; letter-spacing: 1px; }
</style></head><body><div class="card">
  <header><strong>${escape(card.businessName)}</strong><span>Carné de operador</span></header>
  <p class="person"><b>${escape(card.displayName)}</b><small>${escape(card.roleName)} · ${escape(card.userName)}</small></p>
  <div class="barcode">${printedBarcodeSvg(card.token)}<small>…${escape(card.fragment)} · gire el carné para leerlo</small></div>
</div></body></html>`;
}
