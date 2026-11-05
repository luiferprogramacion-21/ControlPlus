import { Routes } from '@angular/router';
import { catalogGuard, cashGuard, salesGuard, inventoryGuard, customersGuard, creditsGuard, suppliersGuard, purchasesGuard, adminGuard, layawaysGuard, sessionGuard } from './core/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'operacion/catalogo' },
  { path: 'ingresar', loadComponent: () => import('./features/auth/login').then(m => m.Login), title: 'Ingresar | ControlPlus' },
  {
    path: 'operacion',
    loadComponent: () => import('./layout/shell').then(m => m.Shell),
    canActivate: [sessionGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'catalogo' },
      { path: 'catalogo', canActivate: [catalogGuard], loadComponent: () => import('./features/catalog/catalog').then(m => m.Catalog), title: 'Catálogo | ControlPlus' },
      { path: 'caja', canActivate: [cashGuard], loadComponent: () => import('./features/cash/cash').then(m => m.Cash), title: 'Caja | ControlPlus' },
      { path: 'inventario', canActivate: [inventoryGuard], loadComponent: () => import('./features/inventory/inventory').then(m => m.Inventory), title: 'Inventario | ControlPlus' },
      { path: 'clientes', canActivate: [customersGuard], loadComponent: () => import('./features/customers/customers').then(m => m.Customers), title: 'Clientes | ControlPlus' },
      { path: 'ventas', canActivate: [salesGuard], loadComponent: () => import('./features/sales/sales').then(m => m.Sales), title: 'Ventas | ControlPlus' },
      { path: 'compras', canActivate: [purchasesGuard], loadComponent: () => import('./features/purchases/purchases').then(m => m.Purchases), title: 'Compras | ControlPlus' },
      { path: 'proveedores', canActivate: [suppliersGuard], loadComponent: () => import('./features/suppliers/suppliers').then(m => m.Suppliers), title: 'Proveedores | ControlPlus' },
      { path: 'apartados', canActivate: [layawaysGuard], loadComponent: () => import('./features/layaways/layaways').then(m => m.Layaways), title: 'Apartados | ControlPlus' },
      { path: 'administracion', canActivate: [adminGuard], loadComponent: () => import('./features/admin/admin').then(m => m.Admin), title: 'Administración | ControlPlus' },
      { path: 'creditos', canActivate: [creditsGuard], loadComponent: () => import('./features/credits/credits').then(m => m.Credits), title: 'Créditos y abonos | ControlPlus' },
    ],
  },
  { path: '**', redirectTo: 'operacion/catalogo' },
];
