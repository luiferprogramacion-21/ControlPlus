import { groupedNumber } from '../../shared/number-input';
import { CorrectionReceipt } from './sales-api';
import { escapeHtml } from './ticket';

const money = (value: number) => `$ ${groupedNumber(value)}`;
const methods: Record<string, string> = { EFECTIVO: 'Efectivo', TRANSFERENCIA: 'Transferencia', NEQUI: 'Nequi' };

/** Uses the authorized, audited snapshot returned by the server; never changes the original ticket. */
export function buildCorrectionReceiptHtml(receipt: CorrectionReceipt): string {
  const correction = receipt.correction;
  const rows = (correction.details ?? []).map(line => `<tr><td>${line.type === 'DEVUELTO' ? 'Se quita' : 'Se entrega'}: ${escapeHtml(line.productName || line.productCode || 'Producto')}<br><small>${escapeHtml(line.productCode || '')} · ${groupedNumber(line.quantity)} × ${money(line.unitPrice)}</small></td><td>${money(line.netAmount)}</td></tr>`).join('');
  const payments = (correction.payments ?? []).map(payment => `<p>${correction.difference < 0 ? 'Devolución' : 'Cobro'} por ${escapeHtml(methods[payment.methodCode] || payment.methodCode)}: ${money(payment.amount)}</p>`).join('');
  const credit = correction.creditAdjustment > 0 ? `<p>${correction.difference < 0 ? 'Reducción' : 'Aumento'} de la deuda: ${money(correction.creditAdjustment)}</p>` : '';
  const occurred = correction.occurredAtUtc ? new Intl.DateTimeFormat('es-CO', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(correction.occurredAtUtc)) : '';
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Corrección ${escapeHtml(correction.visibleNumber)}</title><style>body{font:14px/1.5 sans-serif;color:#17263a;max-width:680px;margin:24px auto;padding:0 18px}h1{font-size:22px}table{width:100%;border-collapse:collapse}td{padding:10px 0;border-bottom:1px solid #ccc}td:last-child{text-align:right;white-space:nowrap}small{color:#555}.total{font-size:18px;font-weight:bold}footer{margin-top:22px;color:#555}</style></head><body>
  <h1>${escapeHtml(receipt.establishmentName)} · Comprobante de corrección</h1><p><strong>${escapeHtml(correction.visibleNumber)}</strong><br>Venta original: ${escapeHtml(receipt.saleNumber)}<br>${escapeHtml(occurred)}</p>
  ${correction.reason ? `<p>Motivo: ${escapeHtml(correction.reason)}</p>` : ''}<table><thead><tr><th>Producto</th><th>Valor</th></tr></thead><tbody>${rows}</tbody></table>
  <p>Valor retirado: ${money(correction.totalReturned)}<br>Valor entregado: ${money(correction.totalDelivered)}</p>
  <p class="total">Variación de la venta: ${money(correction.difference)}</p>${credit}${payments}
  <footer>La venta original se conserva. Este comprobante registra solo esta corrección. Preparar o abrir la impresión no confirma que haya salido el papel.</footer></body></html>`;
}
