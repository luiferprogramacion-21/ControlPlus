import { describeDiscount, discountAmount, maxDiscountAmount, maxDiscountPercent, validateDiscount } from './sale-discount';

describe('sale-discount', () => {
  it('el límite por rol es 5 % Cajero, 20 % Supervisor y 80 % Administrador', () => {
    expect(maxDiscountPercent(false, false)).toBe(5); expect(maxDiscountPercent(false, true)).toBe(20); expect(maxDiscountPercent(true, false)).toBe(80);
  });

  describe('discountAmount', () => {
    it('calcula el porcentaje redondeado a pesos enteros', () => {
      expect(discountAmount(10000, { type: 'PORCENTAJE', value: 10 })).toBe(1000);
      expect(discountAmount(3333, { type: 'PORCENTAJE', value: 5 })).toBe(167);
    });
    it('un valor fijo se descuenta tal cual y nunca supera el valor bruto', () => {
      expect(discountAmount(10000, { type: 'VALOR_FIJO', value: 2500 })).toBe(2500);
      expect(discountAmount(10000, { type: 'VALOR_FIJO', value: 99999 })).toBe(10000);
    });
    it('sin descuento, valor no positivo o bruto cero devuelve cero', () => {
      expect(discountAmount(10000, null)).toBe(0); expect(discountAmount(10000, { type: 'VALOR_FIJO', value: 0 })).toBe(0); expect(discountAmount(0, { type: 'PORCENTAJE', value: 10 })).toBe(0);
    });
  });

  describe('validateDiscount', () => {
    it('acepta el porcentaje hasta el límite del rol y rechaza lo que lo supera', () => {
      expect(validateDiscount({ type: 'PORCENTAJE', value: 5 }, 10000, 5)).toBe('');
      expect(validateDiscount({ type: 'PORCENTAJE', value: 5.5 }, 10000, 5)).toContain('hasta 5 %');
    });
    it('acepta valores en pesos hasta el equivalente al límite del rol', () => {
      expect(maxDiscountAmount(10000, 5)).toBe(500);
      expect(validateDiscount({ type: 'VALOR_FIJO', value: 500 }, 10000, 5)).toBe('');
      expect(validateDiscount({ type: 'VALOR_FIJO', value: 501 }, 10000, 5)).toContain('máximo $ 500');
    });
    it('rechaza ceros, negativos, decimales en pesos y más de lo que vale la venta', () => {
      expect(validateDiscount({ type: 'PORCENTAJE', value: 0 }, 10000, 80)).toContain('mayor que cero');
      expect(validateDiscount({ type: 'VALOR_FIJO', value: -5 }, 10000, 80)).toContain('mayor que cero');
      expect(validateDiscount({ type: 'VALOR_FIJO', value: 100.5 }, 10000, 80)).toContain('entero');
      expect(validateDiscount({ type: 'VALOR_FIJO', value: 10001 }, 10000, 80)).toContain('no puede superar');
      expect(validateDiscount({ type: 'PORCENTAJE', value: 5.123 }, 10000, 80)).toContain('dos decimales');
    });
  });

  it('describe el descuento para la pantalla', () => {
    expect(describeDiscount({ type: 'PORCENTAJE', value: 10 })).toBe('10 %'); expect(describeDiscount({ type: 'VALOR_FIJO', value: 5000 })).toBe('$ 5.000');
  });
});
