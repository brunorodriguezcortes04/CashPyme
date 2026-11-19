import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { SesionService } from './sesion.service';

/**
 * Agrega el JWT a las llamadas a la API. Un 401 en cualquier llamada que no sea el login es un
 * token vencido o inválido: se cierra la sesión y se vuelve al login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const sesion = inject(SesionService);
  const router = inject(Router);
  const esApi = req.url.startsWith(environment.apiUrl);
  const token = sesion.sesion()?.token;

  const pedido = esApi && token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(pedido).pipe(
    catchError((error: unknown) => {
      if (esApi && error instanceof HttpErrorResponse && error.status === 401 && !req.url.endsWith('/auth/login')) {
        void sesion.cerrar().then(() => router.navigateByUrl('/login', { replaceUrl: true }));
      }
      return throwError(() => error);
    })
  );
};
