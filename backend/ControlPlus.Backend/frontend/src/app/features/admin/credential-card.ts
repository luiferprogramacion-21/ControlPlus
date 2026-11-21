import { code128Widths } from '../../core/code128';
import { cardIconSvg } from './credential-card-icon';

export interface CredentialCard { businessName: string; displayName: string; userName: string; roleName: string; token: string; fragment: string; }

const escape = (value: string): string => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

/**
 * The card is printed on the 80 mm roll of the receipt printer (72 mm printable). Since 20 November 2026 it is as large as
 * the roll allows, 71 × 54 mm: the height of a credit card (85.6 × 54 mm), which does not fit across the roll. The exact
 * credit-card size is the downloadable PDF, printed on a normal printer at 100 %.
 */
export const PRINTED_CARD_WIDTH_MM = 71;
export const PRINTED_CARD_HEIGHT_MM = 54;
/**
 * Narrowest bar: 0.5 mm = 4 dots at 203 dpi (20 November 2026). At the store, product labels read only with bars of about
 * 4 dots; with 2 or 3 dots the thermal head closes the spaces and the laser scanner (Honeywell Voyager MS9520) does not
 * even beep. Never scaled by the browser.
 */
export const PRINTED_MODULE_MM = 0.5;
/** Blank margin each side of the bars: at least 10 modules, as Code 128 requires. */
const QUIET_MODULES = 10;
/** Space for the bars across the card. */
const BARCODE_SPACE_MM = PRINTED_CARD_WIDTH_MM - 1;

const modulesOf = (token: string): number => code128Widths(token).reduce((sum, width) => sum + width, 0) + 2 * QUIET_MODULES;

/** True when the code fits across the card with 4-dot bars (the 14-digit cards issued since 20 November 2026 do). */
export function fitsAcross(token: string): boolean { return modulesOf(token) * PRINTED_MODULE_MM <= BARCODE_SPACE_MM; }

/**
 * Code 128 SVG with a fixed size in millimetres (no scaling by the browser). Across the card when it fits (option B, chosen
 * by the user on 20 November 2026: a 14-digit code, about the length of the product labels the scanner reads). An older,
 * longer card code is printed lengthwise instead (option A), so it keeps the same 4-dot bars and only the card grows.
 */
export function printedBarcodeSvg(token: string, barHeightMm = 18): string {
  const widths = code128Widths(token);
  const total = modulesOf(token) * PRINTED_MODULE_MM;
  const across = total <= BARCODE_SPACE_MM;
  const length = across ? barHeightMm : 40;
  let position = QUIET_MODULES * PRINTED_MODULE_MM; let bars = '';
  widths.forEach((width, index) => {
    const size = (width * PRINTED_MODULE_MM).toFixed(4);
    if (index % 2 === 0) bars += across
      ? `<rect x="${position.toFixed(4)}" y="0" width="${size}" height="${length}"/>`
      : `<rect x="0" y="${position.toFixed(4)}" width="${length}" height="${size}"/>`;
    position += width * PRINTED_MODULE_MM;
  });
  const [w, h] = across ? [total.toFixed(4), String(length)] : [String(length), total.toFixed(4)];
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}mm" height="${h}mm" viewBox="0 0 ${w} ${h}" role="img" aria-label="Código de barras del carné" shape-rendering="crispEdges" fill="#000">${bars}</svg>`;
}

/**
 * Card for the 80 mm receipt printer with the barcode the reader scans (redesigned on 20 November 2026 at the user's
 * request so it is not all white). The thermal printer prints black only, so the design is in black: a solid band with
 * the icon and the business name in white, the person's name large, the role in a framed label, and the barcode on white.
 */
export function buildCredentialCardHtml(card: CredentialCard): string {
  const across = fitsAcross(card.token);
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Carné ${escape(card.userName)}</title>
<style>
  @page { size: 80mm auto; margin: 3mm 4.5mm; }
  * { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  body { margin: 0; width: ${PRINTED_CARD_WIDTH_MM}mm; font-family: Arial, Helvetica, sans-serif; color: #000; }
  .card { width: ${PRINTED_CARD_WIDTH_MM}mm; ${across ? `height: ${PRINTED_CARD_HEIGHT_MM}mm;` : ''} display: flex; flex-direction: column; border: 0.4mm solid #000; border-radius: 3mm; overflow: hidden; }
  .band { display: flex; align-items: center; gap: 2.5mm; padding: 2mm 3mm; background: #000; color: #fff; }
  .band svg { flex: none; }
  .band strong { display: block; font-size: 13pt; line-height: 1.1; }
  .band span { display: block; font-size: 6.5pt; letter-spacing: 1.2px; text-transform: uppercase; }
  .rule { height: 0.8mm; margin: 0.8mm 0 0; background: #000; }
  .person { margin: 0; padding: 2mm 3mm 0; }
  .person b { display: block; font-size: 14pt; line-height: 1.15; }
  .person .role { display: inline-block; margin-top: 0.8mm; padding: 0.3mm 1.6mm; border: 0.35mm solid #000; border-radius: 1.2mm; font-size: 8pt; font-weight: bold; text-transform: uppercase; }
  .person .user { font-size: 8.5pt; margin-left: 1.5mm; }
  .barcode { display: flex; flex-direction: column; align-items: center; gap: 0.6mm; margin-top: auto; padding: 1.5mm 0 2mm; }
  .barcode svg { display: block; }
  .barcode small { display: block; font-size: 7.5pt; letter-spacing: 1px; }
</style></head><body><div class="card">
  <header class="band">${cardIconSvg(9, '#fff')}<div><strong>${escape(card.businessName)}</strong><span>Carné de operador</span></div></header>
  <div class="rule"></div>
  <p class="person"><b>${escape(card.displayName)}</b><span class="role">${escape(card.roleName)}</span><span class="user">${escape(card.userName)}</span></p>
  <div class="barcode">${printedBarcodeSvg(card.token, across ? 14 : 18)}<small>…${escape(card.fragment)}${across ? '' : ' · gire el carné para leerlo'}</small></div>
</div></body></html>`;
}
