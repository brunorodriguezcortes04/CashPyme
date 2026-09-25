import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { PantallasService } from './pantallas.service';

/**
 * Deja entrar solo a las pantallas publicadas en la tabla `pantalla` para el rol actual.
 * Compara contra la URL resuelta (no routeConfig.path) para que funcione igual con rutas
 * con parámetros, como movimientos/:tipo. Si la ruta no está publicada, manda a la primera
 * disponible; si no hay ninguna, al inicio.
 */
export const pantallaGuard: CanActivateFn = (route) => {
  const pantallasService = inject(PantallasService);
  const router = inject(Router);
  const ruta = route.url.map((segmento) => segmento.path).join('/');

  return pantallasService.listPantallas().pipe(
    map((pantallas) => {
      if (pantallas.some((pantalla) => pantalla.ruta === ruta)) {
        return true;
      }

      const primera = pantallas[0];
      return router.createUrlTree(primera ? ['/backoffice', ...primera.ruta.split('/')] : ['/']);
    }),
    // No se pudieron resolver las pantallas: sesión vencida o backend caído. Se manda al
    // login con el aviso, en vez de rebotar a la landing sin decir nada (que era lo que
    // hacía parecer que el botón "Ir al backoffice" no funcionaba).
    catchError(() => of(router.createUrlTree(['/login'], { queryParams: { sesionExpirada: 1 } })))
  );
};
