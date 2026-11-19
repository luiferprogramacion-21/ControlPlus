import { buildCorrectionReceiptHtml } from './sale-correction-receipt';

describe('Comprobante de corrección de venta', () => {
  it('muestra corrección, origen, cantidades, deuda y medios sin convertirlo en otro ticket original', () => {
    const html = buildCorrectionReceiptHtml({ requestId: 'r', saleId: 's', saleNumber: 'VTA-001', requestedAtUtc: '', establishmentName: 'Tienda <script>', correction: {
      id: 'c', saleId: 's', visibleNumber: 'CAM-001', totalReturned: 1234567, totalDelivered: 0, difference: -1234567, treatment: 'REDUCIR_CREDITO_Y_DEVOLVER', creditAdjustment: 1000000, paidOrRefunded: 234567,
      details: [{ id: 'l', type: 'DEVUELTO', productId: 'p', originalSaleLineId: 'l1', quantity: 1234, unitPrice: 1000, discount: 0, netAmount: 1234567, productName: '<b>Cuaderno</b>', productCode: 'P-000001' }], payments: [{ methodCode: 'EFECTIVO', amount: 234567, receivedAmount: null, reference: null }] } });
    expect(html).toContain('VTA-001'); expect(html).toContain('CAM-001');
    expect(html).toContain('1.234 × $ 1.000'); expect(html).toContain('Reducción de la deuda: $ 1.000.000');
    expect(html).toContain('Devolución por Efectivo: $ 234.567');
    expect(html).toContain('&lt;b&gt;Cuaderno&lt;/b&gt;'); expect(html).not.toContain('<script>');
  });
});
