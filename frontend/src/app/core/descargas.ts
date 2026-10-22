import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable, catchError, from, map, switchMap } from 'rxjs';
import { apiError } from './api-error';

/** Un archivo que generó el backend, con el nombre que puso en Content-Disposition. */
export interface ArchivoDescargado {
  contenido: Blob;
  nombre: string;
}

/**
 * GET de un archivo generado por el backend (PDF, CSV, Excel). El nombre lo decide el backend;
 * los errores llegan como Blob y se leen para mostrar su mensaje igual que en el resto de la app.
 */
export function descargarArchivo(
  http: HttpClient,
  url: string,
  params: HttpParams,
  nombrePorDefecto: string
): Observable<ArchivoDescargado> {
  return http.get(url, { params, responseType: 'blob', observe: 'response' }).pipe(
    map((respuesta) => ({
      contenido: respuesta.body ?? new Blob(),
      nombre: nombreDeArchivo(respuesta.headers.get('content-disposition')) ?? nombrePorDefecto
    })),
    catchError(errorDeBlob)
  );
}

/** Guarda el archivo en el equipo (el token va en un header, así que no sirve un enlace directo). */
export function guardarArchivo({ contenido, nombre }: ArchivoDescargado): void {
  const url = URL.createObjectURL(contenido);
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = nombre;
  enlace.click();
  URL.revokeObjectURL(url);
}

/** filename="..." de un Content-Disposition, o null si no viene. */
function nombreDeArchivo(contentDisposition: string | null): string | null {
  const coincidencia = /filename="?([^";]+)"?/i.exec(contentDisposition ?? '');
  return coincidencia ? coincidencia[1].trim() : null;
}

/** Con responseType 'blob' el cuerpo del error también es un Blob: se lee como JSON ({ message }). */
function errorDeBlob(error: HttpErrorResponse) {
  if (!(error.error instanceof Blob)) {
    return apiError(error);
  }

  return from(error.error.text()).pipe(
    switchMap((texto) => {
      let cuerpo: unknown = null;
      try {
        cuerpo = JSON.parse(texto);
      } catch {
        // Sin JSON (un 500 del servidor, por ejemplo): apiError usa el mensaje genérico.
      }
      return apiError(new HttpErrorResponse({ ...error, error: cuerpo, url: error.url ?? undefined }));
    })
  );
}
