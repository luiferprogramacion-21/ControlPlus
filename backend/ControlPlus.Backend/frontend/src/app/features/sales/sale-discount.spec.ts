import { costFloorDiscount, describeDiscount, discountAmount, maxDiscountAmount, maxDiscountPercent, validateDiscount } from './sale-discount';

describe('sale-discount', () => {
  it('calcula lo más que se puede descontar sin vender por debajo del costo (pruebas del 26/11/2026)', () => {
    expect(costFloorDiscount([{ gross: 20000, cost: 14000 }])).toBe(6000);
    // Venta total: lo limita el producto de menor margen (20 % aquí) y nunca deja la venta en cero.
    expect(costFloorDiscount([{ gross: 10000, cost: 8000 }, { gross: 10000, cost: 2000 }])).toBe(4000);
    expect(costFloorDiscount([{ gross: 10000, cost: 0 }, { gross: 5000, cost: 0 }])).toBe(14999);
    expect(costFloorDiscount([{ gross: 10000, cost: null }])).toBeNull();
    expect(costFloorDiscount([])).toBeNull();
  });

  it('el límite inicial por rol es 5 % Cajero y 20 % Supervisor; el Administrador no tiene tope (D7) y se usan los configurados', () => {
    expect(maxDiscountPercent(false, false)).toBe(5); expect(maxDiscountPercent(false, true)).toBe(20); expect(maxDiscountPercent(true, false)).toBe(100);
    expect(maxDiscountPercent(false, false, { cashier: 10, supervisor: 25, administrator: 100 })).toBe(10);
    expect(maxDiscountPercent(false, true, { cashier: 10, supervisor: 25, administrator: 100 })).toBe(25);
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
    expect(describeDiscount({ type: 'PORCENTAJE', value: 12.5 })).toBe('12,5 %');
    expect(describeDiscount({ type: 'PORCENTAJE', value: 10 })).toBe('10 %'); expect(describeDiscount({ type: 'VALOR_FIJO', value: 5000 })).toBe('$ 5.000');
  });
});
