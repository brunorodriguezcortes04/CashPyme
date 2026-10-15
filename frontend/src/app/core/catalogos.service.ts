import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

export interface OpcionCatalogo {
  valor: string;
  etiqueta: string;
}

/**
 * Tipo de documento tributario. esNota y signo los decide el backend para que el front no
 * tenga que saber que 'nota_credito' resta: signo es -1 para la nota de crédito y +1 para
 * el resto.
 */
export interface TipoDocumento {
  valor: string;
  etiqueta: string;
  esNota: boolean;
  signo: number;
}

/** Tipo de movimiento con los textos de su pantalla: los define el backend, no el front. */
export interface TipoMovimiento {
  valor: string;
  titulo: string;
  subtitulo: string;
  boton: string;
  tituloListado: string;
  mensajeVacio: string;
  mensajeExito: string;
  ejemploDescripcion: string;
}

/**
 * Tipo de cuenta de un documento (por cobrar o por pagar) con los textos y datos de su
 * pantalla. Una sola pantalla sirve para las dos: lo que cambia entre ellas viene de acá.
 */
export interface TipoCuentaDocumento {
  valor: string;
  titulo: string;
  subtitulo: string;
  tituloNueva: string;
  tituloEditar: string;
  botonRegistrar: string;
  tituloListado: string;
  mensajeVacio: string;
  mensajeRegistrada: string;
  mensajeAnulada: string;
  preguntaAnular: string;
  efectoAnular: string;
  /** "Cliente" o "Proveedor". */
  tercero: string;
  seleccionarTercero: string;
  sinTerceros: string;
  ayudaNota: string;
  ejemploDescripcion: string;
  /** Encabezado de la columna de lo ya cobrado o pagado. */
  columnaAplicado: string;
  /** Botón de la fila: "Cobrar" o "Pagar". */
  accion: string;
  tituloAplicar: string;
  aplicadosPrevios: string;
  mensajeAplicado: string;
  mensajeQuitado: string;
  /** 'ingreso' o 'egreso': el tipo de movimiento con que se paga y el de sus categorías. */
  flujo: string;
  seleccionarMovimiento: string;
  sinMovimientos: string;
  /** Ruta del backoffice donde se registran esos movimientos, p. ej. 'movimientos/ingreso'. */
  rutaMovimientos: string;
  nombrePantallaMovimientos: string;
  /** Ruta de esta pantalla en el backoffice. */
  ruta: string;
  /** Con cuántos días de plazo se propone la fecha de vencimiento. */
  diasVencimientoPorDefecto: number;
}

/** Opciones y textos de dominio. Viven en el backend para que el front no los tenga en duro. */
@Injectable({ providedIn: 'root' })
export class CatalogosService {
  private readonly http = inject(HttpClient);

  listMediosPago(): Observable<OpcionCatalogo[]> {
    return this.http.get<OpcionCatalogo[]>('/api/opciones/medios-pago').pipe(catchError(apiError));
  }

  listTiposCuenta(): Observable<OpcionCatalogo[]> {
    return this.http.get<OpcionCatalogo[]>('/api/opciones/tipos-cuenta').pipe(catchError(apiError));
  }

    listTiposTercero(): Observable<OpcionCatalogo[]> {
    return this.http.get<OpcionCatalogo[]>('/api/opciones/tipos-tercero').pipe(catchError(apiError));
  }

  listTiposDocumento(): Observable<TipoDocumento[]> {
    return this.http.get<TipoDocumento[]>('/api/opciones/tipos-documento').pipe(catchError(apiError));
  }

  listTiposMovimiento(): Observable<TipoMovimiento[]> {
    return this.http.get<TipoMovimiento[]>('/api/opciones/tipos-movimiento').pipe(catchError(apiError));
  }

  listTiposCuentaDocumento(): Observable<TipoCuentaDocumento[]> {
    return this.http
      .get<TipoCuentaDocumento[]>('/api/opciones/tipos-cuenta-documento')
      .pipe(catchError(apiError));
  }
}
