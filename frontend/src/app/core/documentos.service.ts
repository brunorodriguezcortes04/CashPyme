import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

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
  descripcion: string | null;
  fechaCreacion: string;
  /** Lo que sigue sale de vista_documento_saldo, la misma fuente que la proyección de caja. */
  montoPagado: number;
  montoNotasCredito: number;
  montoNotasDebito: number;
  saldoPendiente: number;
  estaVencido: boolean;
  diasAtraso: number;
  /** Documento que esta nota corrige; null si no es una nota. */
  idDocumentoReferencia: number | null;
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

/** tipoCuenta es obligatorio: el listado nunca mezcla lo por cobrar con lo por pagar. */
export interface FiltrosDocumentos {
  tipoCuenta: string;
  estado?: string;
  idTercero?: number;
  pagina?: number;
  tamanoPagina?: number;
}

export interface CrearDocumentoPayload {
  tipoCuenta: string;
  tipoDocumento: string;
  idTercero: number;
  montoTotal: number;
  fechaEmision: string;
  fechaVencimiento: string;
  numeroDocumento?: string | null;
  idCategoria?: number | null;
  montoNeto?: number | null;
  montoIva?: number;
  descripcion?: string | null;
  /** Obligatorio para una nota de crédito o débito; prohibido para el resto. */
  idDocumentoReferencia?: number | null;
}

/** Igual que CrearDocumentoPayload pero sin tipoCuenta: no se puede cambiar al editar. */
export type ActualizarDocumentoPayload = Omit<CrearDocumentoPayload, 'tipoCuenta'>;

@Injectable({ providedIn: 'root' })
export class DocumentosService {
  private readonly http = inject(HttpClient);

  /** Devuelve una página del listado. Una página vacía es un resultado válido, no un error. */
  listDocumentos(filtros: FiltrosDocumentos): Observable<DocumentosPaginados> {
    return this.http
      .get<DocumentosPaginados>('/api/documentos', { params: this.toParams(filtros) })
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
    if (filtros.idTercero) params = params.set('idTercero', filtros.idTercero);
    if (filtros.pagina) params = params.set('pagina', filtros.pagina);
    if (filtros.tamanoPagina) params = params.set('tamanoPagina', filtros.tamanoPagina);
    return params;
  }
}
