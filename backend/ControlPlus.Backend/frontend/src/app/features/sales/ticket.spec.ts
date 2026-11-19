import { buildTicketHtml, escapeHtml, SaleReceipt, vatBreakdown } from './ticket';

const receipt: SaleReceipt = {
  saleId: 's1', visibleNumber: 'VTA-001-000010', occurredAtUtc: '2026-11-06T15:00:00Z', operatingDate: '2026-11-06', saleType: 'CONTADO', state: 'CONFIRMADA',
  grossSubtotal: 55000, discountTotal: 5000, total: 50000, change: 10000,
  lines: [{ id: 'l1', productId: 'p', productCode: 'P-1', productName: 'Camisa <b>azul</b> & más', measurementUnit: 'UND', quantity: 2, priceType: 'MINORISTA', unitPrice: 27500, discountAmount: 5000, grossAmount: 55000, netAmount: 50000 }],
  payments: [{ methodCode: 'EFECTIVO', appliedAmount: 50000, receivedAmount: 60000, change: 10000 }],
};

describe('ticket', () => {
  it('separa los miles de las cantidades sin modificar códigos del comprobante', () => {
    const html = buildTicketHtml({ ...receipt, lines: [{ ...receipt.lines[0], quantity: 1234 }] });
    expect(html).toContain('1.234 x $ 27.500');
    expect(html).toContain('VTA-001-000010');
  });
  it('escapa el HTML de los datos dinámicos', () => {
    expect(escapeHtml(`<a href="x">&'</a>`)).toBe('&lt;a href=&quot;x&quot;&gt;&amp;&#39;&lt;/a&gt;');
    const html = buildTicketHtml(receipt);
    expect(html).toContain('Camisa &lt;b&gt;azul&lt;/b&gt; &amp; más'); expect(html).not.toContain('<b>azul</b>');
  });
  it('muestra número, total, descuento, medio de pago y cambio', () => {
    const html = buildTicketHtml({ ...receipt, establishmentName: 'Tienda Central', establishmentIdentification: '900-1', establishmentPhone: '555', establishmentAddress: 'Ocaña', receiptHeader: 'Compra local', receiptFooter: 'Vuelva pronto' }, { operator: 'Cajero Uno', customer: 'Ana' });
    for (const text of ['VTA-001-000010', 'TOTAL', '$ 50.000', '-$ 5.000', 'Efectivo', 'Cambio', '$ 10.000', 'Atendió: Cajero Uno', 'Cliente: Ana', 'Ticket de venta', 'Tienda Central', 'NIT: 900-1', 'Ocaña', 'Compra local', 'Vuelva pronto']) expect(html).toContain(text);
  });
  it('nunca muestra IVA, aunque la venta antigua lo tenga guardado', () => {
    const html = buildTicketHtml({ ...receipt, vatPercentage: 19, taxableBaseTotal: 42017, vatTotal: 7983 });
    expect(html).not.toContain('IVA'); expect(html).toContain('Subtotal'); expect(html).toContain('TOTAL');
    expect(vatBreakdown({ ...receipt, vatPercentage: 19, taxableBaseTotal: 42017, vatTotal: 7983 })).toBeNull();
  });
  it('marca la reimpresión y la venta a crédito sin pagos', () => {
    const html = buildTicketHtml({ ...receipt, saleType: 'CREDITO', dueDate: '2026-11-30', payments: [], change: 0 }, {}, true);
    expect(html).toContain('REIMPRESIÓN'); expect(html).toContain('Venta a crédito'); expect(html).toContain('Vence: 30 de noviembre de 2026'); expect(html).not.toContain('Cambio');
  });
});
