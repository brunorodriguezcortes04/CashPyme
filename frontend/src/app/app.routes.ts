import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { TIPO_CUENTA_POR_COBRAR, TIPO_CUENTA_POR_PAGAR } from './core/documentos.service';
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
      {
        path: 'resumen',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/resumen/resumen').then((m) => m.Resumen)
      },
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
      // Estas dos rutas tienen que existir mientras las pantallas 'usuarios' y 'cuenta'
      // sigan publicadas en la tabla `pantalla` (migraciones 006 y 007): la nav del
      // backoffice se arma desde ahí, así que borrarlas deja enlaces que no resuelven.
      {
        path: 'usuarios',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/usuarios/usuarios').then((m) => m.Usuarios)
      },
      {
        path: 'cuenta',
        canActivate: [pantallaGuard],
        loadComponent: () => import('./pages/cuenta/cuenta').then((m) => m.Cuenta)
      },
      // Un solo componente para cuentas por cobrar y por pagar. Cada una mantiene su ruta (es
      // la que publica la tabla `pantalla`) y le pasa el tipo de cuenta; los textos y las
      // reglas de cada una vienen del backend.
      {
        path: 'cuentas-por-cobrar',
        canActivate: [pantallaGuard],
        data: { tipoCuenta: TIPO_CUENTA_POR_COBRAR },
        loadComponent: () => import('./pages/documentos/documentos').then((m) => m.Documentos)
      },
      {
        path: 'cuentas-por-pagar',
        canActivate: [pantallaGuard],
        data: { tipoCuenta: TIPO_CUENTA_POR_PAGAR },
        loadComponent: () => import('./pages/documentos/documentos').then((m) => m.Documentos)
      },
      {
        path: 'terceros',
        canActivate: [pantallaGuard],
         loadComponent: () => import('./pages/terceros/terceros').then((m) => m.Terceros)
      }
      
    ]
  }
];
