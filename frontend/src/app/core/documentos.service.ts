import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';
import { Tercero } from './terceros.service';

/** Cuenta por cobrar o por pagar. Tabla documento_financiero en la base. */
export const TIPO_CUENTA_POR_COBRAR = 'por_cobrar';
export const TIPO_CUENTA_POR_PAGAR = 'por_pagar';

export interface Documento {
  id: number;
  tipoCuenta: string;
  tipoDocumento: string;
  numeroDocumento: string | null;
  idTercero: number;
  nombreTercero: string;
  idCategoria: number | null;
  nombreCategoria: string | null;
  montoNeto: number | null;
  montoIva: number;
  montoTotal: number;
  fechaEmision: string;
  fechaVencimiento: string;
  estadoDocumento: string;
  /**
   * Texto del estado para mostrar ("Pagada", "Cerrada por nota", "Vigente"…). Lo arma el
   * backend para que todas las pantallas digan lo mismo.
   */
  etiquetaEstado: string;
  descripcion: string | null;
  fechaCreacion: string;
  /** Lo que sigue lo calcula el backend con la misma fórmula que usa la proyección de caja. */
  montoPagado: number;
  montoNotasCredito: number;
  montoNotasDebito: number;
  saldoPendiente: number;
  estaVencido: boolean;
  diasAtraso: number;
  /** pendiente / vencida / pagada; null para notas y anulados. Lo decide el backend. */
  situacion: string | null;
  /** Documento que esta nota corrige; null si no es una nota. */
  idDocumentoReferencia: number | null;
  /** Qué botones ofrece la fila: lo decide el backend con las mismas reglas con que valida. */
  acciones: {
    puedeAplicar: boolean;
    puedeEmitirNota: boolean;
    puedeEditar: boolean;
    puedeAnular: boolean;
  };
}

/** Documento al que se le puede emitir una nota, con lo que todavía le queda por cobrar o pagar. */
export interface DocumentoCorregible {
  id: number;
  tipoDocumento: string;
  numeroDocumento: string | null;
  idTercero: number;
  nombreTercero: string;
  saldoPendiente: number;
}

/** Movimiento de caja con monto todavía sin aplicar a ningún documento. */
export interface MovimientoAplicable {
  id: number;
  fecha: string;
  monto: number;
  montoDisponible: number;
  medioPago: string;
  nombreCuenta: string;
  descripcion: string | null;
}

export interface Pago {
  id: number;
  idDocumento: number;
  idMovimiento: number;
  montoAplicado: number;
  fechaMovimiento: string;
  medioPago: string;
  descripcionMovimiento: string | null;
}

/** Una página del listado. totalResultados es el total sin paginar, para calcular las páginas. */
export interface DocumentosPaginados {
  items: Documento[];
  totalResultados: number;
  pagina: number;
  tamanoPagina: number;
}

/** Un grupo del listado (vencidas, pendientes, pagadas) con cuántos documentos tiene y cuánto suman. */
export interface ResumenSituacion {
  situacion: string;
  etiqueta: string;
  cantidad: number;
  saldoPendiente: number;
}

/** tipoCuenta es obligatorio: el listado nunca mezcla lo por cobrar con lo por pagar. */
export interface FiltrosDocumentos {
  tipoCuenta: string;
  estado?: string;
  /** pendiente / vencida / pagada (los valores vienen en el resumen). */
  situacion?: string;
  idTercero?: number;
  pagina?: number;
  tamanoPagina?: number;
}

/**
 * Una nota va sin tercero ni vencimiento: el backend le pone el tercero del documento que
 * corrige y el vencimiento igual a la emisión.
 */
export interface CrearDocumentoPayload {
  tipoCuenta: string;
  tipoDocumento: string;
  idTercero: number | null;
  montoTotal: number;
  fechaEmision: string;
  fechaVencimiento: string | null;
  numeroDocumento?: string | null;
  idCategoria?: number | null;
  montoNeto?: number | null;
  montoIva?: number;
  descripcion?: string | null;
  /** Obligatorio para una nota de crédito o débito; prohibido para el resto. */
  idDocumentoReferencia?: number | null;
}

/**
 * Sin tipoCuenta, que no se cambia al editar. Una nota no se edita, así que acá tercero y
 * vencimiento siempre van.
 */
export type ActualizarDocumentoPayload = Omit<
  CrearDocumentoPayload,
  'tipoCuenta' | 'idTercero' | 'fechaVencimiento' | 'idDocumentoReferencia'
> & {
  idTercero: number;
  fechaVencimiento: string;
};

@Injectable({ providedIn: 'root' })
export class DocumentosService {
  private readonly http = inject(HttpClient);

  /** Devuelve una página del listado. Una página vacía es un resultado válido, no un error. */
  listDocumentos(filtros: FiltrosDocumentos): Observable<DocumentosPaginados> {
    return this.http
      .get<DocumentosPaginados>('/api/documentos', { params: this.toParams(filtros) })
      .pipe(catchError(apiError));
  }

  resumen(tipoCuenta: string): Observable<ResumenSituacion[]> {
    return this.http
      .get<ResumenSituacion[]>('/api/documentos/resumen', { params: { tipoCuenta } })
      .pipe(catchError(apiError));
  }

  crear(payload: CrearDocumentoPayload): Observable<Documento> {
    return this.http.post<Documento>('/api/documentos', payload).pipe(catchError(apiError));
  }

  actualizar(id: number, payload: ActualizarDocumentoPayload): Observable<Documento> {
    return this.http.put<Documento>(`/api/documentos/${id}`, payload).pipe(catchError(apiError));
  }

  /**
   * "Eliminar" un documento en realidad lo anula (nunca se borra la fila). Con pagos
   * registrados el backend responde 409 y no hay forma de forzarlo: hay que anular primero
   * esos movimientos de caja.
   */
  anular(id: number): Observable<Documento> {
    return this.http.post<Documento>(`/api/documentos/${id}/anular`, {}).pipe(catchError(apiError));
  }

  /**
   * Clientes o proveedores con los que se puede registrar este tipo de cuenta. El backend
   * aplica la misma regla con la que después valida al guardar.
   */
  listTerceros(tipoCuenta: string): Observable<Tercero[]> {
    return this.http
      .get<Tercero[]>('/api/documentos/terceros', { params: { tipoCuenta } })
      .pipe(catchError(apiError));
  }

  /** Documentos de este tipo de cuenta a los que se les puede emitir una nota. */
  listCorregibles(tipoCuenta: string): Observable<DocumentoCorregible[]> {
    return this.http
      .get<DocumentoCorregible[]>('/api/documentos/corregibles', { params: { tipoCuenta } })
      .pipe(catchError(apiError));
  }

  /** Notas de crédito y débito emitidas sobre un documento. */
  listNotas(idDocumento: number): Observable<Documento[]> {
    return this.http
      .get<Documento[]>(`/api/documentos/${idDocumento}/notas`)
      .pipe(catchError(apiError));
  }

  /** Movimientos de caja con monto libre para aplicar a este tipo de cuenta. */
  listMovimientosAplicables(tipoCuenta: string): Observable<MovimientoAplicable[]> {
    return this.http
      .get<MovimientoAplicable[]>('/api/pagos/movimientos-aplicables', { params: { tipoCuenta } })
      .pipe(catchError(apiError));
  }

  listPagos(idDocumento: number): Observable<Pago[]> {
    return this.http.get<Pago[]>(`/api/pagos/documento/${idDocumento}`).pipe(catchError(apiError));
  }

  /** Aplica un movimiento ya registrado al documento. No crea plata: el movimiento ya existe. */
  crearPago(idDocumento: number, idMovimiento: number, montoAplicado: number): Observable<Pago> {
    return this.http
      .post<Pago>('/api/pagos', { idDocumento, idMovimiento, montoAplicado })
      .pipe(catchError(apiError));
  }

  /** Quita el enlace entre el movimiento y el documento; ninguno de los dos se borra. */
  eliminarPago(idPago: number): Observable<void> {
    return this.http.delete<void>(`/api/pagos/${idPago}`).pipe(catchError(apiError));
  }

  /**
   * Los filtros vacíos no se mandan, para que el backend los trate como "sin filtro" en vez
   * de como un valor vacío que no calza con nada. tipoCuenta sí va siempre.
   */
  private toParams(filtros: FiltrosDocumentos): HttpParams {
    let params = new HttpParams().set('tipoCuenta', filtros.tipoCuenta);
    if (filtros.estado) params = params.set('estado', filtros.estado);
    if (filtros.situacion) params = params.set('situacion', filtros.situacion);
    if (filtros.idTercero) params = params.set('idTercero', filtros.idTercero);
    if (filtros.pagina) params = params.set('pagina', filtros.pagina);
    if (filtros.tamanoPagina) params = params.set('tamanoPagina', filtros.tamanoPagina);
    return params;
  }
}
