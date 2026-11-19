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
    case 'ENVIADO': return 'Cajón: Windows aceptó la orden. Comprueba que se abrió.';
    case 'FALLIDO': return 'Cajón: no se pudo enviar la orden. Revisa la impresora y pide una apertura manual en Caja.';
    case 'INCIERTO': return 'Cajón: no se pudo confirmar el envío. Comprueba el cajón antes de pedir otra apertura.';
    default: return '';
  }
}
