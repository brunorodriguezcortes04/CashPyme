import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SesionService } from './sesion.service';

/** Solo con sesión; si no, al login. */
export const conSesion: CanActivateFn = async () => {
  const router = inject(Router);
  return (await inject(SesionService).cargar()) ? true : router.createUrlTree(['/login']);
};

/** El login solo sin sesión; con sesión, directo al resumen. */
export const sinSesion: CanActivateFn = async () => {
  const router = inject(Router);
  return (await inject(SesionService).cargar()) ? router.createUrlTree(['/resumen']) : true;
};
