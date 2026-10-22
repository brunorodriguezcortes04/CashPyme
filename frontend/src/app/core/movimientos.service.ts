import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { apiError } from './api-error';
import { ArchivoDescargado, descargarArchivo } from './descargas';

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
  /** "Registrado" o "Anulado": lo decide el backend. */
  etiquetaEstado: string;
  fechaAnulacion: string | null;
  fechaCreacion: string;
  /** Qué botones ofrece la fila. */
  acciones: { puedeEditar: boolean; puedeAnular: boolean };
  /** Solo al crear o editar: aviso para mostrar junto al éxito (p. ej. cuenta sobregirada). */
  advertencia?: string | null;
}

/**
 * Una página del listado. totalResultados es el total sin paginar, para calcular las páginas.
 * fechaDesde y fechaHasta son el rango que aplicó el backend (sin rango pedido, el mes en curso
 * de la empresa) y periodoPorDefecto dice si es ese mes.
 */
export interface MovimientosPaginados {
  items: Movimiento[];
  totalResultados: number;
  pagina: number;
  tamanoPagina: number;
  fechaDesde: string | null;
  fechaHasta: string | null;
  periodoPorDefecto: boolean;
}

/**
 * Filtros del listado. Todos opcionales y combinables: los que no se mandan el backend los
 * trata como "sin filtro". Sin rango de fecha el backend asume el mes actual.
 */
export interface FiltrosMovimientos {
  tipo?: string;
  fechaDesde?: string;
  fechaHasta?: string;
  idCategoria?: number;
  idCuenta?: number;
  pagina?: number;
  tamanoPagina?: number;
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

  /** Devuelve una página del listado. Una página vacía es un resultado válido, no un error. */
  listMovimientos(filtros: FiltrosMovimientos): Observable<MovimientosPaginados> {
    return this.http
      .get<MovimientosPaginados>('/api/movimientos', { params: this.toParams(filtros) })
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

  /**
   * El listado filtrado completo como CSV o Excel. Manda los mismos filtros que el listado (sin
   * paginar): el backend aplica exactamente el mismo filtro.
   */
  exportar(filtros: FiltrosMovimientos, formato: 'csv' | 'xlsx'): Observable<ArchivoDescargado> {
    const { tipo, fechaDesde, fechaHasta, idCategoria, idCuenta } = filtros;
    return descargarArchivo(
      this.http,
      '/api/movimientos/export',
      this.toParams({ tipo, fechaDesde, fechaHasta, idCategoria, idCuenta }).set('formato', formato),
      `movimientos.${formato}`
    );
  }

  /**
   * Los filtros vacíos no se mandan, para que el backend los trate como "sin filtro" en vez
   * de como un valor vacío que no calza con nada.
   */
  private toParams(filtros: FiltrosMovimientos): HttpParams {
    let params = new HttpParams();
    if (filtros.tipo) params = params.set('tipo', filtros.tipo);
    if (filtros.fechaDesde) params = params.set('fechaDesde', filtros.fechaDesde);
    if (filtros.fechaHasta) params = params.set('fechaHasta', filtros.fechaHasta);
    if (filtros.idCategoria) params = params.set('idCategoria', filtros.idCategoria);
    if (filtros.idCuenta) params = params.set('idCuenta', filtros.idCuenta);
    if (filtros.pagina) params = params.set('pagina', filtros.pagina);
    if (filtros.tamanoPagina) params = params.set('tamanoPagina', filtros.tamanoPagina);
    return params;
  }
}
