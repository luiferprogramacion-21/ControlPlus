import { groupedNumber } from '../../shared/number-input';
import { CorrectionReceipt } from './sales-api';
import { escapeHtml } from './ticket';

const money = (value: number) => `$ ${groupedNumber(value)}`;
const methods: Record<string, string> = { EFECTIVO: 'Efectivo', TRANSFERENCIA: 'Transferencia', NEQUI: 'Nequi' };

/**
 * Uses the authorized, audited snapshot returned by the server; never changes the original ticket.
 * Auditoría del 23 de noviembre de 2026: formato de ticket de 80 mm como el de venta, compacto y sin datos repetidos. Con
 * márgenes de página mínimos el navegador no imprime su encabezado ni su pie (la dirección «localhost» y la fecha).
 */
export function buildCorrectionReceiptHtml(receipt: CorrectionReceipt): string {
  const correction = receipt.correction;
  const rows = (correction.details ?? []).map(line => `<tr><td>${line.type === 'DEVUELTO' ? '−' : '+'} ${groupedNumber(line.quantity)} ${escapeHtml(line.productName || line.productCode || 'Producto')}</td><td class="r">${money(line.netAmount)}</td></tr>`).join('');
  const refund = correction.difference < 0;
  const result = correction.difference === 0
    ? '<tr class="total"><td>Sin diferencia</td><td class="r">$ 0</td></tr>'
    : `<tr class="total"><td>${refund ? 'Se devuelve' : 'Se cobra'}</td><td class="r">${money(Math.abs(correction.difference))}</td></tr>`;
  const payments = (correction.payments ?? []).map(payment => `<tr><td>${escapeHtml(methods[payment.methodCode] || payment.methodCode)}</td><td class="r">${money(payment.amount)}</td></tr>`).join('');
  const credit = correction.creditAdjustment > 0
    ? `<tr><td>${refund ? 'Baja la deuda' : 'Sube la deuda'}</td><td class="r">${money(correction.creditAdjustment)}</td></tr>` : '';
  const occurred = correction.occurredAtUtc ? new Intl.DateTimeFormat('es-CO', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(correction.occurredAtUtc)) : '';
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Corrección ${escapeHtml(correction.visibleNumber)}</title><style>
@page { size: 80mm auto; margin: 3mm; }
* { box-sizing: border-box; } body { width: 74mm; margin: 0; font: 12px/1.35 'Courier New', monospace; color: #000; }
h1 { margin: 0; font-size: 16px; text-align: center; } .c { text-align: center; } .r { text-align: right; white-space: nowrap; }
table { width: 100%; border-collapse: collapse; } td { padding: 1px 0; vertical-align: top; } hr { border: 0; border-top: 1px dashed #000; margin: 6px 0; }
.total td { font-size: 14px; font-weight: bold; }
</style></head><body>
<h1>${escapeHtml(receipt.establishmentName)}</h1>
<p class="c">CORRECCIÓN ${escapeHtml(correction.visibleNumber)}<br>Venta ${escapeHtml(receipt.saleNumber)}<br>${escapeHtml(occurred)}</p>
${correction.reason ? `<p>Motivo: ${escapeHtml(correction.reason)}</p>` : ''}
<hr><table>${rows}</table><hr>
<table>${result}${payments}${credit}</table>
<hr><p class="c">La venta original se conserva.</p></body></html>`;
}
