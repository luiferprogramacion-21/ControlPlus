import { buildTicketHtml, escapeHtml, SaleReceipt, vatBreakdown } from './ticket';

const receipt: SaleReceipt = {
  saleId: 's1', visibleNumber: 'VTA-001-000010', occurredAtUtc: '2026-11-06T15:00:00Z', operatingDate: '2026-11-06', saleType: 'CONTADO', state: 'CONFIRMADA',
  grossSubtotal: 55000, discountTotal: 5000, total: 50000, change: 10000,
  lines: [{ id: 'l1', productId: 'p', productCode: 'P-1', productName: 'Camisa <b>azul</b> & más', measurementUnit: 'UND', quantity: 2, priceType: 'MINORISTA', unitPrice: 27500, discountAmount: 5000, grossAmount: 55000, netAmount: 50000 }],
  payments: [{ methodCode: 'EFECTIVO', appliedAmount: 50000, receivedAmount: 60000, change: 10000 }],
};

describe('ticket', () => {
  it('escapa el HTML de los datos dinámicos', () => {
    expect(escapeHtml(`<a href="x">&'</a>`)).toBe('&lt;a href=&quot;x&quot;&gt;&amp;&#39;&lt;/a&gt;');
    const html = buildTicketHtml(receipt);
    expect(html).toContain('Camisa &lt;b&gt;azul&lt;/b&gt; &amp; más'); expect(html).not.toContain('<b>azul</b>');
  });
  it('muestra número, total, descuento, medio de pago y cambio', () => {
    const html = buildTicketHtml({ ...receipt, establishmentName: 'Tienda Central', establishmentIdentification: '900-1', establishmentPhone: '555', establishmentAddress: 'Ocaña', receiptHeader: 'Compra local', receiptFooter: 'Vuelva pronto' }, { operator: 'Cajero Uno', customer: 'Ana' });
    for (const text of ['VTA-001-000010', 'TOTAL', '$ 50.000', '-$ 5.000', 'Efectivo', 'Cambio', '$ 10.000', 'Atendió: Cajero Uno', 'Cliente: Ana', 'Ticket de venta', 'Tienda Central', 'NIT: 900-1', 'Ocaña', 'Compra local', 'Vuelva pronto']) expect(html).toContain(text);
  });
  describe('desglose de IVA', () => {
    // Price with VAT included: Subtotal + IVA = TOTAL, and the total never changes.
    const withVat = { ...receipt, grossSubtotal: 50000, discountTotal: 0, total: 50000, vatPercentage: 19, taxableBaseTotal: 42017, vatTotal: 7983 };
    const order = (html: string, labels: string[]) => labels.map(label => html.indexOf(label));
    it('muestra Subtotal, IVA y TOTAL en ese orden, con el precio ya incluyendo el IVA', () => {
      const html = buildTicketHtml(withVat);
      const positions = order(html, ['Subtotal', 'IVA 19 %', 'TOTAL']);
      expect(positions.every(position => position >= 0)).toBe(true); expect(positions).toEqual([...positions].sort((a, b) => a - b));
      expect(html).toContain('$ 42.017'); expect(html).toContain('$ 7.983'); expect(html).toContain('$ 50.000');
      expect(html).not.toContain('incluido'); expect(html).not.toContain('Base gravable'); expect(html).not.toContain('Valor productos');
    });
    it('con descuento agrega Valor productos y Descuento antes del Subtotal', () => {
      const html = buildTicketHtml({ ...withVat, grossSubtotal: 55000, discountTotal: 5000 });
      const positions = order(html, ['Valor productos', 'Descuento', 'Subtotal', 'IVA 19 %', 'TOTAL']);
      expect(positions.every(position => position >= 0)).toBe(true); expect(positions).toEqual([...positions].sort((a, b) => a - b));
      expect(html).toContain('$ 55.000'); expect(html).toContain('-$ 5.000');
    });
    it('no aparece si el IVA estaba apagado (sin datos, nulos o porcentaje cero) y el resumen queda como siempre', () => {
      for (const sale of [receipt, { ...receipt, vatPercentage: null, taxableBaseTotal: null, vatTotal: null }, { ...receipt, vatPercentage: 0, taxableBaseTotal: 50000, vatTotal: 0 }]) {
        const html = buildTicketHtml(sale);
        expect(html).not.toContain('IVA'); expect(html).not.toContain('Base gravable'); expect(html).not.toContain('Valor productos');
        expect(html).toContain('Subtotal'); expect(html).toContain('$ 55.000');
      }
    });
    it('no aparece con datos incompletos ni inválidos', () => {
      expect(vatBreakdown({ ...receipt, vatPercentage: 19, taxableBaseTotal: 42017 })).toBeNull();
      expect(vatBreakdown({ ...receipt, vatPercentage: Number.NaN, taxableBaseTotal: 1, vatTotal: 1 })).toBeNull();
    });
    it('formatea un porcentaje con decimales y se conserva en crédito y reimpresión', () => {
      const html = buildTicketHtml({ ...withVat, vatPercentage: 19.5, payments: [], change: 0 }, {}, true);
      expect(html).toContain('IVA 19,5 %'); expect(html).toContain('Venta a crédito'); expect(html).toContain('REIMPRESIÓN');
    });
  });
  it('marca la reimpresión y la venta a crédito sin pagos', () => {
    const html = buildTicketHtml({ ...receipt, saleType: 'CREDITO', dueDate: '2026-11-30', payments: [], change: 0 }, {}, true);
    expect(html).toContain('REIMPRESIÓN'); expect(html).toContain('Venta a crédito'); expect(html).toContain('Vence: 30 de noviembre de 2026'); expect(html).not.toContain('Cambio');
  });
});
