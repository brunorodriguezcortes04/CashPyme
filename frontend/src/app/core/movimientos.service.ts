import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
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
}
