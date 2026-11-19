import { profitPercentOf, suggestedPrice } from './price-calculator';

describe('calculadora de precio', () => {
  it('suma la ganancia al costo y redondea hacia arriba de 50 en 50', () => {
    expect(suggestedPrice(10000, 30)).toBe(13000);
    expect(suggestedPrice(7000, 30)).toBe(9100);
    expect(suggestedPrice(1234, 25)).toBe(1550);
    expect(suggestedPrice(1000, 0)).toBe(1000);
  });
  it('no inventa un precio con datos inválidos', () => {
    expect(suggestedPrice(null, 30)).toBeNull();
    expect(suggestedPrice(0, 30)).toBeNull();
    expect(suggestedPrice(1000, null)).toBeNull();
    expect(suggestedPrice(1000, -5)).toBeNull();
    expect(suggestedPrice(Number.NaN, 30)).toBeNull();
  });
  it('calcula cuánto se gana con un precio', () => {
    expect(profitPercentOf(13000, 10000)).toBe(30);
    expect(profitPercentOf(10000, 10000)).toBe(0);
    expect(profitPercentOf(9000, 10000)).toBe(-10);
    expect(profitPercentOf(1550, 1234)).toBe(25.6);
    expect(profitPercentOf(1000, 0)).toBeNull();
    expect(profitPercentOf(null, 1000)).toBeNull();
  });
});
