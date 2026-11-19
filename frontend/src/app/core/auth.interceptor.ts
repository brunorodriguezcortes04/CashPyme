import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * El login y el registro crean una sesión nueva, así que van sin token aunque haya uno
 * guardado (a /login se puede llegar con la sesión abierta). Su 401 siempre es
 * "credenciales incorrectas": no puede cerrar la sesión que ya estaba ni pasar por vencida.
 */
const RUTAS_SIN_SESION = ['/api/auth/login', '/api/auth/register'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.getToken();

  if (!token || !req.url.startsWith('/api') || RUTAS_SIN_SESION.includes(req.url)) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: HttpErrorResponse) => {
      // Un 401 en una petición que SÍ llevaba token: venció, o el backend lo revocó porque
      // la contraseña cambió en otro dispositivo o desactivaron al usuario. Sin esto la
      // sesión quedaba medio viva: el menú seguía en pantalla y cada acción fallaba con un
      // mensaje que culpaba al usuario de un problema de sesión.
      // Solo cuenta si el token rechazado sigue siendo el de la sesión: una petición que
      // salió antes de cambiar la contraseña vuelve con 401 para el token viejo, y eso no
      // puede llevarse la sesión nueva.
      if (error.status === 401 && authService.getToken() === token) {
        authService.logout();
        router.navigate(['/login'], { queryParams: { sesionExpirada: 1 } });
      }

      return throwError(() => error);
    })
  );
};
