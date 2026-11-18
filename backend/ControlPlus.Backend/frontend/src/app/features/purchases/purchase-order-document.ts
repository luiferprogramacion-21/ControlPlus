import { inject, Injectable } from '@angular/core';
import { Establishment, EstablishmentService } from '../../core/establishment';
import { Supplier } from '../suppliers/suppliers-api';
import { escapeHtml, printTicket } from '../sales/ticket';
import { Order } from './orders-api';

const formatDate = (value: string): string => new Intl.DateTimeFormat('es-CO', {
  dateStyle: 'long', timeStyle: 'short', timeZone: 'America/Bogota',
}).format(new Date(value));

const supplierDocument = (supplier: Supplier): string => supplier.documentNumber
  ? `${supplier.documentType ?? 'Documento'} ${supplier.documentNumber}`
  : 'Sin documento registrado';

/** Builds the supplier-facing purchase order generated when an order is sent. */
export function buildPurchaseOrderHtml(order: Order, supplier: Supplier, operatorName = '', establishment?: Establishment | null): string {
  const rows = order.lines.map((line, index) => `<tr>
    <td class="index">${index + 1}</td>
    <td><strong>${escapeHtml(line.productName)}</strong><small>${escapeHtml(line.productCode)}</small></td>
    <td class="quantity">${line.requestedQuantity.toLocaleString('es-CO')}</td>
    <td>${line.observation ? escapeHtml(line.observation) : '—'}</td>
  </tr>`).join('');
  const contact = [supplier.phone, supplier.email, supplier.address].filter(Boolean).map(value => escapeHtml(value!)).join(' · ');
  const totalUnits = order.lines.reduce((sum, line) => sum + line.requestedQuantity, 0);
  const businessName = establishment?.commercialName || 'Baratísimo';
  const businessContact = [establishment?.identification ? `NIT ${establishment.identification}` : null,
    establishment?.phone, establishment?.address].filter(Boolean).map(value => escapeHtml(value!)).join(' · ');
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Pedido ${escapeHtml(order.visibleNumber)}</title><style>
@page { size: A4 portrait; margin: 14mm; }
* { box-sizing: border-box; } body { margin: 0; color: #17263a; font: 12px/1.45 Arial, sans-serif; }
header { display: flex; justify-content: space-between; gap: 24px; padding-bottom: 18px; border-bottom: 3px solid #245da8; }
.brand { color: #245da8; font-size: 24px; font-weight: 800; letter-spacing: -.5px; } .brand small { display: block; color: #607087; font-size: 10px; font-weight: 600; letter-spacing: 1.5px; text-transform: uppercase; }
.document { text-align: right; } .document h1 { margin: 0 0 4px; font-size: 21px; } .document strong { color: #245da8; font-size: 15px; }
.meta { display: grid; grid-template-columns: 1.2fr .8fr; gap: 14px; margin: 18px 0; }
.card { padding: 13px 15px; border: 1px solid #cbd5e1; border-radius: 8px; } .label { display: block; margin-bottom: 5px; color: #607087; font-size: 9px; font-weight: 700; letter-spacing: 1px; text-transform: uppercase; }
.card b { display: block; font-size: 14px; } .card p { margin: 4px 0 0; color: #526176; }
table { width: 100%; border-collapse: collapse; } th { padding: 9px 10px; background: #eaf0f8; color: #35465d; font-size: 10px; letter-spacing: .5px; text-align: left; text-transform: uppercase; }
td { padding: 10px; border-bottom: 1px solid #d9e1eb; vertical-align: top; } td small { display: block; margin-top: 2px; color: #607087; } .index { width: 38px; } .quantity { width: 95px; font-size: 14px; font-weight: 800; text-align: right; }
.summary { display: flex; justify-content: flex-end; gap: 28px; margin-top: 14px; padding: 12px 14px; background: #f5f8fc; } .summary span { color: #607087; } .summary strong { margin-left: 8px; font-size: 15px; }
.signatures { display: grid; grid-template-columns: 1fr 1fr; gap: 70px; margin-top: 58px; } .signature { padding-top: 7px; border-top: 1px solid #68788c; text-align: center; }
footer { position: fixed; right: 0; bottom: 0; left: 0; color: #718096; font-size: 9px; text-align: center; }
</style></head><body>
<header><div class="brand">${escapeHtml(businessName)}<small>ControlPlus · Pedido a proveedor${businessContact ? `<br>${businessContact}` : ''}</small></div><div class="document"><h1>Pedido de compra</h1><strong>${escapeHtml(order.visibleNumber)}</strong><div>${escapeHtml(formatDate(order.sentAt ?? order.createdAt))}</div></div></header>
<section class="meta"><div class="card"><span class="label">Proveedor</span><b>${escapeHtml(supplier.name)}</b><p>${escapeHtml(supplierDocument(supplier))}${contact ? `<br>${contact}` : ''}</p></div><div class="card"><span class="label">Información del pedido</span><b>${order.lines.length} producto(s) · ${totalUnits.toLocaleString('es-CO')} unidades</b><p>Estado: Enviado${operatorName ? `<br>Generado por ${escapeHtml(operatorName)}` : ''}</p></div></section>
<table><thead><tr><th>#</th><th>Producto</th><th class="quantity">Cantidad</th><th>Observación</th></tr></thead><tbody>${rows}</tbody></table>
<div class="summary"><span>Total de productos <strong>${order.lines.length}</strong></span><span>Total de unidades <strong>${totalUnits.toLocaleString('es-CO')}</strong></span></div>
<section class="signatures"><div class="signature">Elaboró</div><div class="signature">Recibido por el proveedor</div></section>
<footer>Documento generado por ControlPlus · ${escapeHtml(order.visibleNumber)}</footer>
</body></html>`;
}

@Injectable({ providedIn: 'root' })
export class PurchaseOrderDocumentPrinter {
  private readonly establishment = inject(EstablishmentService);

  async print(order: Order, supplier: Supplier, operatorName = ''): Promise<void> {
    let business = this.establishment.value();
    if (!business) try { business = await this.establishment.load(); } catch { /* se conserva el nombre de respaldo */ }
    return printTicket(buildPurchaseOrderHtml(order, supplier, operatorName, business));
  }
}
