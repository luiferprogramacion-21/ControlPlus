import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session.service';

export const sessionGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);
  return await session.validate() ? true : router.parseUrl('/ingresar');
};

export const catalogGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  return session.has('PRODUCTS.READ') || session.has('CATEGORIES.READ');
};
export const cashGuard: CanActivateFn = () => inject(SessionService).has('CASH.OWN_READ');
export const salesGuard: CanActivateFn = () => inject(SessionService).has('SALES.CREATE');

export const inventoryGuard: CanActivateFn = () => inject(SessionService).has('INVENTORY.READ');
export const customersGuard: CanActivateFn = () => { const session = inject(SessionService); return session.has('CUSTOMERS.BASIC_MANAGE') || session.has('CUSTOMERS.MANAGE'); };
export const creditsGuard: CanActivateFn = () => inject(SessionService).has('CREDIT.MANAGE');

export const adminGuard: CanActivateFn = () => { const session = inject(SessionService); return session.has('USERS.READ') || session.has('ROLES.READ'); };
export const layawaysGuard: CanActivateFn = () => inject(SessionService).has('LAYAWAY.MANAGE');
export const suppliersGuard: CanActivateFn = () => inject(SessionService).has('SUPPLIERS.READ');
export const purchasesGuard: CanActivateFn = () => { const session = inject(SessionService); return session.has('PURCHASES.RECEIVE') || session.has('PURCHASE_ORDERS.MANAGE'); };
