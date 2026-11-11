import { returnValue, settle } from './sale-change-math';

describe('sale-change-math', () => {
  describe('returnValue', () => {
    it('devuelve el neto unitario por la cantidad', () => expect(returnValue(30000, 3, 0, 2)).toBe(20000));
    it('trunca el neto unitario y deja el resto para la última devolución', () => {
      expect(returnValue(10000, 3, 0, 1)).toBe(3333);
      expect(returnValue(10000, 3, 2, 1)).toBe(3334);
      expect(returnValue(10000, 3, 0, 3)).toBe(10000);
    });
    it('ignora cantidades inválidas', () => { expect(returnValue(10000, 0, 0, 1)).toBe(0); expect(returnValue(10000, 2, 0, 0)).toBe(0); });
  });

  describe('settle', () => {
    it('sin diferencia usa AUTO y no liquida nada', () => expect(settle('CONTADO', 0, null, '')).toMatchObject({ treatment: 'AUTO', mode: 'none', cashAmount: 0, error: '' }));
    it('contado con diferencia positiva cobra', () => expect(settle('CONTADO', 5000, null, '')).toMatchObject({ treatment: 'AUTO', mode: 'payment', cashAmount: 5000 }));
    it('contado con diferencia negativa devuelve', () => expect(settle('CONTADO', -5000, null, '')).toMatchObject({ treatment: 'AUTO', mode: 'refund', cashAmount: 5000 }));
    it('crédito con diferencia positiva exige elegir', () => expect(settle('CREDITO', 5000, 20000, '')).toMatchObject({ needsChoice: true, error: expect.stringContaining('Elige') }));
    it('crédito: cobrar ahora', () => expect(settle('CREDITO', 5000, 20000, 'PAGO_INMEDIATO')).toMatchObject({ treatment: 'PAGO_INMEDIATO', mode: 'payment', cashAmount: 5000, creditAdjustment: 0 }));
    it('crédito: sumar al saldo', () => expect(settle('CREDITO', 5000, 20000, 'AGREGAR_CREDITO')).toMatchObject({ treatment: 'AGREGAR_CREDITO', mode: 'none', cashAmount: 0, creditAdjustment: 5000 }));
    it('crédito con diferencia negativa reduce el saldo', () => expect(settle('CREDITO', -5000, 20000, '')).toMatchObject({ treatment: 'AUTO', mode: 'none', cashAmount: 0, creditAdjustment: -5000 }));
    it('crédito: lo que excede el saldo se devuelve', () => expect(settle('CREDITO', -30000, 20000, '')).toMatchObject({ treatment: 'AUTO', mode: 'refund', cashAmount: 10000, creditAdjustment: -20000 }));
    it('crédito sin saldo reducible no se puede liquidar', () => expect(settle('CREDITO', -5000, 0, '').error).toContain('saldo'));
  });
});
