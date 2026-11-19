import { Injectable } from '@angular/core';
import { SaleLine } from './sales-api';

export interface SaleReceipt {
  saleId: string; visibleNumber: string; occurredAtUtc: string; operatingDate: string; saleType: string; state: string;
  grossSubtotal: number; discountTotal: number; total: number; change: number;
  operatorName?: string | null; customerName?: string | null;
  dueDate?: string | null;
  establishmentName?: string | null; establishmentIdentification?: string | null;
  establishmentPhone?: string | null; establishmentAddress?: string | null;
  receiptHeader?: string | null; receiptFooter?: string | null;
  /** VAT breakdown saved with the sale. Absent or null when the business had VAT switched off, so the ticket shows nothing. */
  vatPercentage?: number | null; taxableBaseTotal?: number | null; vatTotal?: number | null;
  lines: SaleLine[]; payments: { methodCode: string; appliedAmount: number; receivedAmount: number | null; change: number }[];
}
export interface TicketExtras { operator?: string; customer?: string; }

const METHOD_NAMES: Record<string, string> = { EFECTIVO: 'Efectivo', TRANSFERENCIA: 'Transferencia', NEQUI: 'Nequi' };
const cop = (value: number): string => `$ ${Math.round(Number.isFinite(value) ? value : 0).toLocaleString('es-CO')}`;
const percent = (value: number): string => new Intl.NumberFormat('es-CO', { maximumFractionDigits: 2 }).format(value);
const multiline = (value: string): string => escapeHtml(value.trim()).replace(/\r\n?|\n/g, '<br>');
const longDate = (value: string): string => new Intl.DateTimeFormat('es-CO', { dateStyle: 'long', timeZone: 'UTC' })
  .format(new Date(`${value}T00:00:00Z`));

/** The VAT lines appear only for sales that saved a breakdown, i.e. made while VAT was switched on. The price already includes it. */
export function vatBreakdown(receipt: SaleReceipt): { label: string; base: number; vat: number } | null {
  // El IVA se quitó del sistema el 18 de noviembre de 2026: el ticket ya no lo muestra, ni siquiera en ventas antiguas.
  if (receipt) return null;
  const { vatPercentage: rate, taxableBaseTotal: base, vatTotal: vat } = receipt;
  if (typeof rate !== 'number' || typeof base !== 'number' || typeof vat !== 'number') return null;
  if (!Number.isFinite(rate) || !Number.isFinite(base) || !Number.isFinite(vat) || rate <= 0) return null;
  return { label: `IVA ${percent(rate)} %`, base, vat };
}

export const escapeHtml = (value: string): string => value.replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch] as string);

/** Builds a self-contained 80 mm ticket document. Every dynamic value is escaped. */
export function buildTicketHtml(receipt: SaleReceipt, extras: TicketExtras = {}, reprint = false): string {
  const date = new Intl.DateTimeFormat('es-CO', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(receipt.occurredAtUtc));
  const lines = receipt.lines.map(line =>
    `<tr><td colspan="2">${escapeHtml(line.productName)}</td></tr>` +
    `<tr><td class="muted">${line.quantity.toLocaleString('es-CO')} x ${cop(line.unitPrice)}</td><td class="r">${cop(line.netAmount)}</td></tr>`).join('');
  const vat = vatBreakdown(receipt);
  const discountRow = receipt.discountTotal ? `<tr><td>Descuento</td><td class="r">-${cop(receipt.discountTotal)}</td></tr>` : '';
  // With VAT: Subtotal + IVA = TOTAL (the price includes it, the total never changes). Without VAT the summary is unchanged.
  const summary = vat
    ? `${receipt.discountTotal ? `<tr><td>Valor productos</td><td class="r">${cop(receipt.grossSubtotal)}</td></tr>${discountRow}` : ''}` +
      `<tr><td>Subtotal</td><td class="r">${cop(vat.base)}</td></tr><tr><td>${escapeHtml(vat.label)}</td><td class="r">${cop(vat.vat)}</td></tr>`
    : `<tr><td>Subtotal</td><td class="r">${cop(receipt.grossSubtotal)}</td></tr>${discountRow}`;
  const payments = receipt.payments.map(payment =>
    `<tr><td>${escapeHtml(METHOD_NAMES[payment.methodCode] ?? payment.methodCode)}</td><td class="r">${cop(payment.appliedAmount)}</td></tr>`).join('');
  const businessName = receipt.establishmentName?.trim() || 'Baratísimo';
  const businessData = [receipt.establishmentIdentification ? `NIT: ${receipt.establishmentIdentification}` : '',
    receipt.establishmentAddress ?? '', receipt.establishmentPhone ? `Tel: ${receipt.establishmentPhone}` : '']
    .filter(Boolean).map(value => escapeHtml(value)).join('<br>');
  const header = receipt.receiptHeader?.trim() ? `<p class="c receipt-copy">${multiline(receipt.receiptHeader)}</p>` : '';
  const footer = receipt.receiptFooter?.trim() ? multiline(receipt.receiptFooter) : '¡Gracias por su compra!';
  const dueDate = receipt.dueDate && receipt.saleType === 'CREDITO'
    ? `<br><strong>Vence: ${escapeHtml(longDate(receipt.dueDate))}</strong>` : '';
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Ticket ${escapeHtml(receipt.visibleNumber)}</title><style>
@page { size: 80mm auto; margin: 3mm; }
* { box-sizing: border-box; } body { width: 74mm; margin: 0; font: 12px/1.35 'Courier New', monospace; color: #000; }
h1 { margin: 0; font-size: 16px; text-align: center; } .c { text-align: center; } .r { text-align: right; white-space: nowrap; } .muted { color: #333; } .business-data, .receipt-copy { margin: 2px 0; }
table { width: 100%; border-collapse: collapse; } td { padding: 1px 0; vertical-align: top; } hr { border: 0; border-top: 1px dashed #000; margin: 6px 0; }
.total td { font-size: 15px; font-weight: bold; }
</style></head><body>
${header}<h1>${escapeHtml(businessName)}</h1>${businessData ? `<p class="c business-data">${businessData}</p>` : ''}<p class="c">${reprint ? 'REIMPRESIÓN' : 'Ticket de venta'}<br>${escapeHtml(receipt.visibleNumber)}<br>${escapeHtml(date)}</p>
${extras.operator ? `<p>Atendió: ${escapeHtml(extras.operator)}</p>` : ''}${extras.customer ? `<p>Cliente: ${escapeHtml(extras.customer)}</p>` : ''}
<hr><table>${lines}</table><hr>
<table>${summary}
<tr class="total"><td>TOTAL</td><td class="r">${cop(receipt.total)}</td></tr></table>
${receipt.payments.length ? `<hr><table>${payments}${receipt.change > 0 ? `<tr><td>Cambio</td><td class="r">${cop(receipt.change)}</td></tr>` : ''}</table>` : `<hr><p class="c">Venta a crédito${dueDate}</p>`}
<hr><p class="c">${footer}</p></body></html>`;
}

/** Prints through a hidden frame so the page and its styles stay untouched. Resolves when the print dialog was opened. */
export function printTicket(html: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const frame = document.createElement('iframe');
    frame.setAttribute('aria-hidden', 'true');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;';
    frame.onload = () => {
      try {
        frame.contentWindow?.focus(); frame.contentWindow?.print();
        setTimeout(() => frame.remove(), 1000); resolve();
      } catch (error) { frame.remove(); reject(error); }
    };
    frame.srcdoc = html;
    document.body.appendChild(frame);
  });
}

/** Injectable so that the screens (and their tests) never depend on the browser print dialog directly. */
@Injectable({ providedIn: 'root' })
export class TicketPrinter {
  print(html: string): Promise<void> { return printTicket(html); }
}
