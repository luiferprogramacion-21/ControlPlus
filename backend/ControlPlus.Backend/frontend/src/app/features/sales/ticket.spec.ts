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
    const html = buildTicketHtml(receipt, { operator: 'Cajero Uno', customer: 'Ana' });
    for (const text of ['VTA-001-000010', 'TOTAL', '$ 50.000', '-$ 5.000', 'Efectivo', 'Cambio', '$ 10.000', 'Atendió: Cajero Uno', 'Cliente: Ana', 'Ticket de venta']) expect(html).toContain(text);
  });
  describe('desglose de IVA', () => {
    const withVat = { ...receipt, vatPercentage: 19, taxableBaseTotal: 42017, vatTotal: 7983 };
    it('aparece con la base y el IVA incluido cuando la venta lo guardó', () => {
      const html = buildTicketHtml(withVat);
      expect(html).toContain('Base gravable'); expect(html).toContain('$ 42.017'); expect(html).toContain('IVA 19 % incluido'); expect(html).toContain('$ 7.983');
      expect(html.indexOf('TOTAL')).toBeLessThan(html.indexOf('Base gravable'));
    });
    it('no aparece si el IVA estaba apagado (sin datos, nulos o porcentaje cero)', () => {
      for (const sale of [receipt, { ...receipt, vatPercentage: null, taxableBaseTotal: null, vatTotal: null }, { ...receipt, vatPercentage: 0, taxableBaseTotal: 50000, vatTotal: 0 }]) {
        const html = buildTicketHtml(sale);
        expect(html).not.toContain('Base gravable'); expect(html).not.toContain('IVA');
      }
    });
    it('no aparece con datos incompletos ni inválidos', () => {
      expect(vatBreakdown({ ...receipt, vatPercentage: 19, taxableBaseTotal: 42017 })).toBeNull();
      expect(vatBreakdown({ ...receipt, vatPercentage: Number.NaN, taxableBaseTotal: 1, vatTotal: 1 })).toBeNull();
    });
    it('formatea un porcentaje con decimales y se conserva en crédito y reimpresión', () => {
      const html = buildTicketHtml({ ...withVat, vatPercentage: 19.5, payments: [], change: 0 }, {}, true);
      expect(html).toContain('IVA 19,5 % incluido'); expect(html).toContain('Venta a crédito'); expect(html).toContain('REIMPRESIÓN');
    });
  });
  it('marca la reimpresión y la venta a crédito sin pagos', () => {
    const html = buildTicketHtml({ ...receipt, payments: [], change: 0 }, {}, true);
    expect(html).toContain('REIMPRESIÓN'); expect(html).toContain('Venta a crédito'); expect(html).not.toContain('Cambio');
  });
});
