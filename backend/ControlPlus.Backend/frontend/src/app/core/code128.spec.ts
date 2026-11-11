import { code128Svg, code128Values, code128Widths } from './code128';

describe('Code 128', () => {
  it('calcula el dígito de control del ejemplo clásico PJJ123C con inicio B', () => {
    const values = code128Values('PJJ123C');
    expect(values[0]).toBe(104);
    expect(values.at(-2)).toBe(55);
    expect(values.at(-1)).toBe(106);
  });

  it('cada símbolo suma 11 módulos y la parada 13', () => {
    const all = Array.from({ length: 95 }, (_, i) => String.fromCharCode(32 + i)).join('');
    const widths = code128Widths(all);
    const symbols = code128Values(all).length;
    expect(widths.reduce((a, b) => a + b, 0)).toBe((symbols - 1) * 11 + 13);
  });

  it('un número de dígitos pares usa el subconjunto C (dos dígitos por símbolo)', () => {
    const values = code128Values('123456');
    expect(values).toEqual([105, 12, 34, 56, 44, 106]);
    expect(code128Values('0123456789'.repeat(5) + '12345678').length).toBe(58 / 2 + 3);
  });

  it('un token hexadecimal de 48 caracteres genera un SVG con barras', () => {
    const svg = code128Svg('0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF');
    expect(svg.startsWith('<svg')).toBe(true);
    expect(svg).toContain('<rect');
  });

  it('rechaza vacío y caracteres no admitidos', () => {
    expect(() => code128Values('')).toThrow();
    expect(() => code128Values('Ñ')).toThrow();
  });
});
