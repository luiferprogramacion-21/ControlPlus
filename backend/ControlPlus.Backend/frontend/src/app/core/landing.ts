/** Lo mínimo del estado de Caja que decide adónde llega quien vende. */
export interface LandingCash { hasOpenShift: boolean; requiresPreviousShiftClosure: boolean; }

/**
 * Pantalla de llegada al iniciar sesión, según el mapa de navegación oficial (maqueta 00, «Destino según rol y caja»):
 * - Administrador o Supervisor que ve el Dashboard: Dashboard.
 * - Quien vende: punto de venta si hay turno abierto; Caja si falta abrirlo o hay un turno anterior sin cerrar.
 * - Los demás: Catálogo, como antes.
 * `cash` es null si no se pudo consultar Caja; en ese caso va al punto de venta, que explica qué falta.
 */
export const HOME_ROUTE = '/operacion/inicio';

/**
 * Desde el 19 de noviembre de 2026 todos llegan a Inicio, que muestra las tareas del rol y el estado de la caja
 * (decisión «Pantalla de Inicio»). `landingRoute` se conserva como el destino directo de cada rol.
 */
export function landingRoute(
  can: (permission: string) => boolean,
  isRole: (...codes: string[]) => boolean,
  cash: LandingCash | null,
): string {
  if (isRole('ADMINISTRADOR', 'SUPERVISOR') && can('REPORTS.FINANCIAL_READ')) return '/operacion/dashboard';
  if (can('SALES.CREATE')) {
    const needsCash = cash !== null && (!cash.hasOpenShift || cash.requiresPreviousShiftClosure);
    return needsCash && can('CASH.OWN_READ') ? '/operacion/caja' : '/operacion/ventas';
  }
  return '/operacion/catalogo';
}

/** Quien vende necesita conocer el estado de Caja para decidir; los demás no. */
export function landingNeedsCash(can: (permission: string) => boolean, isRole: (...codes: string[]) => boolean): boolean {
  return !(isRole('ADMINISTRADOR', 'SUPERVISOR') && can('REPORTS.FINANCIAL_READ')) && can('SALES.CREATE');
}
