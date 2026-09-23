import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { apiError } from './api-error';

export interface Categoria {
  id: number;
  nombreCategoria: string;
  tipoCategoria: string;
}

export interface Movimiento {
  id: number;
  tipoMovimiento: string;
  monto: number;
  fecha: string;
  idCuenta: number;
  nombreCuenta: string;
  idCategoria: number;
  nombreCategoria: string;
  medioPago: string;
  descripcion: string | null;
  estadoMovimiento: string;
  fechaAnulacion: string | null;
  fechaCreacion: string;
}

export interface CrearMovimientoPayload {
  tipoMovimiento: string;
  fecha: string;
  monto: number;
  idCuenta: number;
  idCategoria: number;
  medioPago: string;
  descripcion?: string | null;
}

/** Igual que CrearMovimientoPayload pero sin tipoMovimiento: no se puede cambiar al editar. */
export type ActualizarMovimientoPayload = Omit<CrearMovimientoPayload, 'tipoMovimiento'>;

/**
 * Se lanza cuando el backend rechaza una anulación porque el movimiento tiene pagos
 * aplicados (409) y todavía no se confirmó. El componente la distingue de un error
 * cualquiera para ofrecer el botón "Confirmar de todas formas".
 */
export class RequiereConfirmacionError extends Error {
  readonly requiereConfirmacion = true;
}

@Injectable({ providedIn: 'root' })
export class MovimientosService {
  private readonly http = inject(HttpClient);

  listCategorias(tipo: string): Observable<Categoria[]> {
    return this.http.get<Categoria[]>('/api/categorias', { params: { tipo } }).pipe(catchError(apiError));
  }

  listMovimientos(tipo?: string): Observable<Movimiento[]> {
    return this.http
      .get<Movimiento[]>('/api/movimientos', { params: tipo ? { tipo } : {} })
      .pipe(catchError(apiError));
  }

  crearMovimiento(payload: CrearMovimientoPayload): Observable<Movimiento> {
    return this.http.post<Movimiento>('/api/movimientos', payload).pipe(catchError(apiError));
  }

  actualizarMovimiento(id: number, payload: ActualizarMovimientoPayload): Observable<Movimiento> {
    return this.http.put<Movimiento>(`/api/movimientos/${id}`, payload).pipe(catchError(apiError));
  }

  /**
   * "Eliminar" un movimiento en realidad lo anula (nunca se borra la fila). Si tiene pagos
   * aplicados y `confirmar` es false, el backend responde 409 y acá se convierte en
   * RequiereConfirmacionError para que el componente ofrezca confirmar antes de reintentar.
   */
  anularMovimiento(id: number, motivo: string | null, confirmar = false): Observable<Movimiento> {
    return this.http
      .post<Movimiento>(`/api/movimientos/${id}/anular`, { motivo, confirmar })
      .pipe(
        catchError((error: HttpErrorResponse) => {
          if (error.status === 409 && !confirmar) {
            const mensaje = error.error?.message ?? 'Este movimiento tiene pagos aplicados.';
            return throwError(() => new RequiereConfirmacionError(mensaje));
          }
          return apiError(error);
        })
      );
  }
}