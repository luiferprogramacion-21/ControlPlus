import { aggregateCashFlow, barHeights, defaultRange, originLabel, rangeDays, validateRange } from './reports-math';

describe('reports-math: rango de fechas', () => {
  it('acepta rangos de 1 a 31 días inclusivos', () => {
    expect(validateRange('2026-11-01', '2026-11-01')).toBe('');
    expect(validateRange('2026-10-01', '2026-10-31')).toBe('');
    expect(rangeDays('2026-10-01', '2026-10-31')).toBe(31);
  });
  it('rechaza más de 31 días, fechas invertidas, vacías o inexistentes', () => {
    expect(validateRange('2026-10-01', '2026-11-01')).toContain('31 días');
    expect(validateRange('2026-11-02', '2026-11-01')).toContain('posterior');
    expect(validateRange('', '2026-11-01')).toContain('Indica');
    expect(validateRange('2026-02-30', '2026-03-01')).toContain('no son válidas');
    expect(validateRange('01/11/2026', '2026-11-02')).toContain('no son válidas');
  });
  it('cuenta correctamente a través de un cambio de mes y de año bisiesto', () => {
    expect(rangeDays('2028-02-28', '2028-03-01')).toBe(3);
    expect(rangeDays('2026-12-31', '2027-01-01')).toBe(2);
  });
  it('el rango predeterminado son los últimos siete días y es válido', () => {
    const range = defaultRange(new Date(2026, 10, 10, 23, 30));
    expect(range).toEqual({ from: '2026-11-04', to: '2026-11-10' });
    expect(validateRange(range.from, range.to)).toBe('');
  });
});

describe('reports-math: serie diaria', () => {
  it('escala las barras al mayor día y deja los días vacíos en cero', () => {
    expect(barHeights([0, 50, 100])).toEqual([0, 50, 100]);
    expect(barHeights([0, 0, 0])).toEqual([0, 0, 0]);
    expect(barHeights([-5, 10])).toEqual([0, 100]);
  });
  it('un valor muy pequeño sigue siendo visible', () => { expect(barHeights([1, 1_000_000])[0]).toBe(2); });
});

describe('reports-math: flujo de caja', () => {
  it('suma por origen y medio conservando el signo y separa cobros de manuales', () => {
    const entry = (origin: string, method: string | null, amount: number) => ({ date: 'd', origin, method, amount });
    const rows = aggregateCashFlow([
      { date: '2026-11-01', openingBalance: 0, payments: [entry('VENTA_CONTADO', 'EFECTIVO', 100), entry('DEVOLUCION_CAMBIO_VENTA', 'EFECTIVO', -30)], manualMovements: [entry('GASTO_MANUAL', null, -20)], paymentTotal: 70, manualTotal: -20 },
      { date: '2026-11-02', openingBalance: 0, payments: [entry('VENTA_CONTADO', 'EFECTIVO', 50)], manualMovements: [], paymentTotal: 50, manualTotal: 0 },
    ]);
    expect(rows.find(row => row.origin === 'VENTA_CONTADO')?.total).toBe(150);
    expect(rows.find(row => row.origin === 'DEVOLUCION_CAMBIO_VENTA')?.total).toBe(-30);
    expect(rows.find(row => row.kind === 'Manual')?.total).toBe(-20);
    expect(rows).toHaveLength(3);
  });
  it('traduce los orígenes conocidos y deja legible uno nuevo', () => {
    expect(originLabel('ABONO_CREDITO')).toBe('Abono de crédito');
    expect(originLabel('REVERSO_VENTA')).toBe('Reverso venta');
  });
});
