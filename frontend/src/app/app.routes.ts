import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { pantallaGuard } from './core/pantalla.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then((m) => m.Home) },
  { path: 'login', loadComponent: () => import('./pages/login/login').then((m) => m.Login) },
  { path: 'registro', loadComponent: () => import('./pages/register/register').then((m) => m.Register) },
  {
    path: 'olvide-password',
    loadComponent: () => import('./pages/olvide-password/olvide-password').then((m) => m.OlvidePassword)
  },
  // Rutas de los enlaces que llegan por correo (ver CorreoCuentaService en el backend).
  {
    path: 'restablecer-password',
    loadComponent: () =>
      import('./pages/restablecer-password/restablecer-password').then((m) => m.RestablecerPassword)
  },
  {
    path: 'verificar-email',
    loadComponent: () => import('./pages/verificar-email/verificar-email').then((m) => m.VerificarEmail)
  },
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
      },
      {
        path: 'usuarios',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/usuarios/usuarios').then((m) => m.Usuarios)
      },
      {
        path: 'cuenta',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/cuenta/cuenta').then((m) => m.Cuenta)
      }
    ]
  }
];
