import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

export interface OpcionCatalogo {
  valor: string;
  etiqueta: string;
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

  listTiposMovimiento(): Observable<TipoMovimiento[]> {
    return this.http.get<TipoMovimiento[]>('/api/opciones/tipos-movimiento').pipe(catchError(apiError));
  }
}
