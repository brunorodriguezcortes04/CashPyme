import { HttpErrorResponse } from '@angular/common/http';
import { throwError } from 'rxjs';

/** Mensaje mostrable a partir de lo que llega al bloque error de un subscribe. */
export function mensajeDeError(error: unknown): string {
  return error instanceof Error ? error.message : 'Ocurrió un error inesperado. Inténtalo nuevamente.';
}

/** Traduce un error HTTP a un mensaje para el usuario. Para usar con catchError. */
export function apiError(error: HttpErrorResponse) {
  if (error.status === 0) {
    return throwError(
      () => new Error('No pudimos conectar con el servidor. Verifica tu conexión e inténtalo de nuevo.')
    );
  }

  if (error.status === 403) {
    return throwError(() => new Error('Tu rol no tiene permiso para realizar esta acción.'));
  }

  if (error.status >= 500) {
    return throwError(() => new Error('Tuvimos un problema de nuestro lado. Inténtalo de nuevo en unos minutos.'));
  }

  // Los errores de negocio traen su propio mensaje (ver ApiExceptionHandler): siempre gana,
  // porque es el único que sabe qué pasó de verdad.
  if (error.error?.message) {
    return throwError(() => new Error(error.error.message));
  }

  // Un 401 SIN cuerpo no lo produjo la aplicación sino el middleware del JWT: token vencido
  // o inválido. Las credenciales incorrectas del login sí traen mensaje, así que salen por
  // el if de arriba y conservan el suyo.
  if (error.status === 401) {
    return throwError(() => new Error('Tu sesión expiró. Vuelve a iniciar sesión.'));
  }

  return throwError(
    () => new Error('No pudimos completar la acción. Revisa los datos e inténtalo de nuevo.')
  );
}
