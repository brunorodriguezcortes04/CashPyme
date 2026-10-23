import { Routes } from '@angular/router';
import { conSesion, sinSesion } from './core/sesion.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'resumen' },
  {
    path: 'login',
    canActivate: [sinSesion],
    loadComponent: () => import('./pages/login/login').then((m) => m.Login)
  },
  {
    path: 'resumen',
    canActivate: [conSesion],
    loadComponent: () => import('./pages/resumen/resumen').then((m) => m.Resumen)
  },
  {
    path: 'alertas',
    canActivate: [conSesion],
    loadComponent: () => import('./pages/alertas/alertas').then((m) => m.Alertas)
  },
  { path: '**', redirectTo: 'resumen' }
];
