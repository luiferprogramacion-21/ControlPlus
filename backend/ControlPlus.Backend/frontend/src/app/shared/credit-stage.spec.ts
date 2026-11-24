import { creditStageView } from './credit-stage';

describe('Etapa del crédito del cliente (auditoría C.14)', () => {
  it('da a cada etapa su texto y su color', () => {
    expect(creditStageView({ stage: 'VENCIDO', outstandingBalance: 80000, overdueBalance: 50000, oldestOverdueOn: '2026-11-20' }))
      .toEqual({ label: 'Moroso', tone: 'error', detail: 'Debe $50.000 vencido desde el viernes, 20 de noviembre de 2026 (total $80.000).' });
    expect(creditStageView({ stage: 'POR_VENCER', outstandingBalance: 30000, overdueBalance: 0, nextDueOn: '2026-11-26' }))
      .toEqual({ label: 'Por vencer', tone: 'warning', detail: 'Debe $30.000; vence el jueves, 26 de noviembre de 2026.' });
    expect(creditStageView({ stage: 'AL_DIA', outstandingBalance: 30000, overdueBalance: 0, nextDueOn: '2026-12-24' })?.tone).toBe('success');
    expect(creditStageView({ stage: 'SIN_DEUDA', outstandingBalance: 0, overdueBalance: 0 })?.tone).toBe('neutral');
    expect(creditStageView(null)).toBeNull();
  });
});
