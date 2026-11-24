import { groupedNumber } from './number-input';
import { dayDate } from './day-date';

/** Etapa del crédito de un cliente que calcula el servidor (CustomerCreditStagePolicy, auditoría C.14). */
export type CreditStage = 'SIN_DEUDA' | 'AL_DIA' | 'POR_VENCER' | 'VENCIDO';

export interface CreditStageData {
  stage: CreditStage | string;
  outstandingBalance: number;
  overdueBalance: number;
  nextDueOn?: string | null;
  oldestOverdueOn?: string | null;
}

/** Texto y color de cada etapa: verde al día, naranja por vencer y rojo moroso. Sin deuda no se resalta. */
export function creditStageView(data: CreditStageData | null | undefined):
  { label: string; detail: string; tone: 'success' | 'warning' | 'error' | 'neutral' } | null {
  if (!data) return null;
  const money = (value: number) => `$${groupedNumber(value)}`;
  switch (data.stage) {
    case 'VENCIDO':
      return { label: 'Moroso', tone: 'error',
        detail: `Debe ${money(data.overdueBalance)} vencido${data.oldestOverdueOn ? ` desde el ${dayDate(data.oldestOverdueOn)}` : ''} (total ${money(data.outstandingBalance)}).` };
    case 'POR_VENCER':
      return { label: 'Por vencer', tone: 'warning',
        detail: `Debe ${money(data.outstandingBalance)}${data.nextDueOn ? `; vence el ${dayDate(data.nextDueOn)}` : ''}.` };
    case 'AL_DIA':
      return { label: 'Crédito al día', tone: 'success',
        detail: `Debe ${money(data.outstandingBalance)}${data.nextDueOn ? `; próximo vencimiento el ${dayDate(data.nextDueOn)}` : ''}.` };
    default:
      return { label: 'Sin deuda', tone: 'neutral', detail: 'No tiene créditos pendientes.' };
  }
}
