import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.getToken();

  // El login y el registro van sin token: salen por acá y nunca entran al manejo de 401 de
  // abajo, así que unas credenciales equivocadas siguen mostrando su propio mensaje.
  if (!token || !req.url.startsWith('/api')) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: HttpErrorResponse) => {
      // Un 401 en una petición que SÍ llevaba token solo puede ser token vencido o inválido.
      // Sin esto la sesión quedaba medio viva: el menú seguía en pantalla y cada acción
      // fallaba con un mensaje que culpaba al usuario de un problema de sesión.
      if (error.status === 401) {
        authService.logout();
        router.navigate(['/login'], { queryParams: { sesionExpirada: 1 } });
      }

      return throwError(() => error);
    })
  );
};
