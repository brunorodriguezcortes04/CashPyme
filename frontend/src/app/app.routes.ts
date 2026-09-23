import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { pantallaGuard } from './core/pantalla.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then((m) => m.Home) },
  { path: 'login', loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  { path: 'registro', loadComponent: () => import('./pages/register/register').then((m) => m.Register) },
  {
    path: 'backoffice',
    canActivate: [authGuard],
    loadComponent: () => import('./backoffice/backoffice-shell').then((m) => m.BackofficeShell),
    children: [
      { path: '', redirectTo: 'movimientos/ingreso', pathMatch: 'full' },
      // Un solo componente para ingresos y egresos: el tipo viaja en la URL y sale de
      // pantalla.ruta (ver PantallasService), no de una lista hardcodeada en el front.
      {
        path: 'movimientos/:tipo',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/movimientos/movimientos').then((m) => m.Movimientos)
      },
      {
        path: 'cuentas',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/cuentas/cuentas').then((m) => m.Cuentas)
      },
      {
        path: 'empresa',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/empresa/empresa').then((m) => m.EmpresaPage)
      }
    ]
  }
];
