import { buildPurchaseOrderHtml } from './purchase-order-document';
import { Order } from './orders-api';
import { Supplier } from '../suppliers/suppliers-api';

describe('purchase order document', () => {
  const order: Order = {
    id: 'order', supplierId: 'supplier', status: 'ENVIADO', visibleNumber: 'PED-001-000027', createdAt: '2026-11-15T15:30:00Z', version: 2,
    sentAt: '2026-11-15T15:31:00Z', closedAt: null,
    lines: [{ id: 'line', productId: 'product', productCode: 'PAP-000010', productName: 'Resma <carta>', requestedQuantity: 12, receivedQuantity: 0, pendingQuantity: 12, excessQuantity: 0, suggestedQuantity: 12, estimatedUnitCost: null, observation: 'Caja "sellada"' }],
  };
  const supplier = { id: 'supplier', name: 'Proveedor & Cía.', documentType: 'NIT', documentNumber: '900123', phone: '555', address: 'Centro', email: 'ventas@example.com', observations: null, isActive: true, version: 1 } satisfies Supplier;

  it('genera un documento A4 con proveedor, pedido, productos y totales escapados', () => {
    const html = buildPurchaseOrderHtml(order, supplier, 'Numael Álvarez');
    for (const value of ['Pedido de compra', 'PED-001-000027', 'Proveedor &amp; Cía.', 'Resma &lt;carta&gt;', 'Caja &quot;sellada&quot;', 'Total de unidades', '12', 'Numael Álvarez']) expect(html).toContain(value);
    expect(html).toContain('@page { size: A4 portrait;');
    expect(html).not.toContain('Resma <carta>');
  });
});
