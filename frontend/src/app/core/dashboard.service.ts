import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

/**
 * Saldo en caja + por cobrar - por pagar, con el desglose. Lo calcula el backend
 * (DashboardService): el front solo lo muestra, no vuelve a sumar nada.
 */
export interface SaldoNeto {
  saldoCaja: number;
  porCobrar: number;
  porPagar: number;
  saldoNeto: number;
  documentosPorCobrar: number;
  documentosPorPagar: number;
}

export interface SaldoCuenta {
  id: number;
  nombreCuenta: string;
  tipoCuenta: string;
  saldoActual: number;
}

export interface FlujoDia {
  fecha: string;
  ingresos: number;
  egresos: number;
}

/** Lo calcula el backend (DashboardService.ObtenerCajaActualAsync): el front solo lo muestra. */
export interface CajaActual {
  desde: string;
  hasta: string;
  saldoTotal: number;
  ingresos: number;
  egresos: number;
  flujoNeto: number;
  cuentas: SaldoCuenta[];
  flujoDiario: FlujoDia[];
}

/** Un monto del mes contra el del mes anterior. porcentaje es null si el anterior fue 0. */
export interface Variacion {
  actual: number;
  anterior: number;
  diferencia: number;
  porcentaje: number | null;
  /** Si el cambio es bueno para la caja (lo decide el backend); null si no cambió o no hay base. */
  favorable: boolean | null;
}

export interface CategoriaRanking {
  posicion: number;
  idCategoria: number;
  nombreCategoria: string;
  monto: number;
  cantidadMovimientos: number;
  /** Porcentaje del total de su tipo en el mes. */
  participacion: number;
}

/** Variación contra el mes anterior y ranking de categorías: lo calcula el backend. */
export interface Indicadores {
  desde: string;
  hasta: string;
  desdeAnterior: string;
  hastaAnterior: string;
  ingresos: Variacion;
  egresos: Variacion;
  flujoNeto: Variacion;
  topIngresos: CategoriaRanking[];
  topEgresos: CategoriaRanking[];
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  /** Sin año ni mes, el backend devuelve el mes en curso de la empresa. */
  getCajaActual(anio?: number, mes?: number): Observable<CajaActual> {
    const params: Record<string, number> = {};
    if (anio !== undefined && mes !== undefined) {
      params['anio'] = anio;
      params['mes'] = mes;
    }
    return this.http.get<CajaActual>('/api/dashboard/caja', { params }).pipe(catchError(apiError));
  }

  /** Mismo período que la caja: sin año ni mes, el mes en curso de la empresa. */
  getIndicadores(anio?: number, mes?: number): Observable<Indicadores> {
    const params: Record<string, number> = {};
    if (anio !== undefined && mes !== undefined) {
      params['anio'] = anio;
      params['mes'] = mes;
    }
    return this.http.get<Indicadores>('/api/dashboard/indicadores', { params }).pipe(catchError(apiError));
  }

  getSaldoNeto(): Observable<SaldoNeto> {
    return this.http.get<SaldoNeto>('/api/dashboard/saldo-neto').pipe(catchError(apiError));
  }
}
