import { HttpResponse } from '@angular/common/http';

export type CashDrawerDispatchStatus = 'ENVIADO' | 'FALLIDO' | 'INCIERTO';
export interface CashDrawerDispatch { drawerStatus?: CashDrawerDispatchStatus | null; }

/** La venta o el abono ya se confirmó; el encabezado informa únicamente sobre la orden al cajón. */
export function withCashDrawerStatus<T extends object>(response: HttpResponse<T>): T & CashDrawerDispatch {
  const status = response.headers.get('X-Cash-Drawer-Status');
  return { ...response.body!, drawerStatus: status === 'ENVIADO' || status === 'FALLIDO' || status === 'INCIERTO' ? status : null };
}

export function cashDrawerNotice(status?: CashDrawerDispatchStatus | null): string {
  switch (status) {
    case 'ENVIADO': return 'Cajón: se envió la orden de abrir. Comprueba que se abrió.';
    case 'FALLIDO': return 'Cajón: no se pudo abrir. Revisa la impresora y pide a un Supervisor que lo abra desde Caja.';
    case 'INCIERTO': return 'Cajón: no se sabe si se abrió. Míralo antes de pedir que lo abran otra vez.';
    default: return '';
  }
}
