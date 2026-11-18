import { code128Svg } from '../../core/code128';

export interface CredentialCard { businessName: string; displayName: string; userName: string; roleName: string; token: string; fragment: string; }

const escape = (value: string): string => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

/** Credit-card sized (85.6 × 54 mm) card with the Code 128 barcode the reader scans. Printed once; the token is never stored. */
export function buildCredentialCardHtml(card: CredentialCard): string {
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Carné ${escape(card.userName)}</title>
<style>
  @page { size: 85.6mm 54mm; margin: 0; }
  * { box-sizing: border-box; }
  body { margin: 0; width: 85.6mm; height: 54mm; font-family: Arial, Helvetica, sans-serif; color: #000; }
  .card { width: 100%; height: 100%; padding: 4mm 5mm; display: flex; flex-direction: column; justify-content: space-between; border: 0.3mm solid #000; }
  header { display: flex; justify-content: space-between; align-items: baseline; }
  header strong { font-size: 13pt; letter-spacing: .5px; }
  header span { font-size: 7pt; text-transform: uppercase; }
  .person { margin: 0; }
  .person b { display: block; font-size: 11pt; }
  .person small { font-size: 8pt; }
  .barcode { text-align: center; }
  .barcode svg { width: 100% !important; height: 15mm !important; }
  .barcode small { display: block; font-size: 7pt; letter-spacing: 1px; }
</style></head><body><div class="card">
  <header><strong>${escape(card.businessName)}</strong><span>Credencial de operador</span></header>
  <p class="person"><b>${escape(card.displayName)}</b><small>${escape(card.roleName)} · ${escape(card.userName)}</small></p>
  <div class="barcode">${code128Svg(card.token, 1.4, 60)}<small>…${escape(card.fragment)}</small></div>
</div></body></html>`;
}
