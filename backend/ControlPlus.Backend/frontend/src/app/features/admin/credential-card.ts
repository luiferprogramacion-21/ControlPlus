import { code128Widths } from '../../core/code128';

export interface CredentialCard { businessName: string; displayName: string; userName: string; roleName: string; token: string; fragment: string; }

const escape = (value: string): string => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

/**
 * Printed card size (19 November 2026): it fits the 80 mm roll of the receipt printer (72 mm printable), which is where the
 * card is printed. The older 85.6 mm layout did not fit: the browser shrank it and the bars came out thinner than one dot
 * of the thermal head, so the reader could not decode them.
 */
export const PRINTED_CARD_WIDTH_MM = 70;
export const PRINTED_CARD_HEIGHT_MM = 50;
/** Narrowest bar: 0.25 mm = 2 dots at 203 dpi (the usual 10 mil). Never scaled, so every bar prints sharp. */
export const PRINTED_MODULE_MM = 0.25;
/** Blank margin each side of the bars: at least 10 modules, as Code 128 requires. */
const QUIET_MODULES = 10;

/** Code 128 SVG with a fixed size in millimetres (no scaling by the browser). Narrower modules only for an older, longer code. */
export function printedBarcodeSvg(token: string, maxWidthMm: number, heightMm = 14): string {
  const widths = code128Widths(token);
  const modules = widths.reduce((sum, width) => sum + width, 0) + 2 * QUIET_MODULES;
  const module = Math.min(PRINTED_MODULE_MM, maxWidthMm / modules);
  let x = QUIET_MODULES * module; let bars = '';
  widths.forEach((width, index) => {
    if (index % 2 === 0) bars += `<rect x="${x.toFixed(4)}" y="0" width="${(width * module).toFixed(4)}" height="${heightMm}"/>`;
    x += width * module;
  });
  const total = (modules * module).toFixed(4);
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${total}mm" height="${heightMm}mm" viewBox="0 0 ${total} ${heightMm}" role="img" aria-label="Código de barras Code 128" shape-rendering="crispEdges" fill="#000">${bars}</svg>`;
}

/** Card for the 80 mm receipt printer with the Code 128 barcode the reader scans. Printed once; the token is never stored. */
export function buildCredentialCardHtml(card: CredentialCard): string {
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Carné ${escape(card.userName)}</title>
<style>
  @page { size: 80mm auto; margin: 3mm 5mm; }
  * { box-sizing: border-box; }
  body { margin: 0; width: ${PRINTED_CARD_WIDTH_MM}mm; font-family: Arial, Helvetica, sans-serif; color: #000; }
  .card { width: ${PRINTED_CARD_WIDTH_MM}mm; height: ${PRINTED_CARD_HEIGHT_MM}mm; padding: 3mm 0; display: flex; flex-direction: column; justify-content: space-between; border: 0.3mm dashed #000; }
  header, .person { padding: 0 3mm; }
  header { display: flex; justify-content: space-between; align-items: baseline; gap: 2mm; }
  header strong { font-size: 12pt; }
  header span { font-size: 7pt; text-transform: uppercase; white-space: nowrap; }
  .person { margin: 0; }
  .person b { display: block; font-size: 11pt; }
  .person small { font-size: 8pt; }
  .barcode { display: flex; flex-direction: column; align-items: center; }
  .barcode svg { display: block; }
  .barcode small { display: block; font-size: 7pt; letter-spacing: 1px; }
</style></head><body><div class="card">
  <header><strong>${escape(card.businessName)}</strong><span>Carné de operador</span></header>
  <p class="person"><b>${escape(card.displayName)}</b><small>${escape(card.roleName)} · ${escape(card.userName)}</small></p>
  <div class="barcode">${printedBarcodeSvg(card.token, PRINTED_CARD_WIDTH_MM - 1)}<small>…${escape(card.fragment)}</small></div>
</div></body></html>`;
}
