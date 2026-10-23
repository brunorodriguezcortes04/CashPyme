import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

/** GET /dashboard/caja: el mismo que usa la pantalla "Caja" de la web. */
export interface CajaActual {
  desde: string;
  hasta: string;
  saldoTotal: number;
  ingresos: number;
  egresos: number;
  flujoNeto: number;
  cuentas: { id: number; nombreCuenta: string; saldoActual: number }[];
}

export interface Movimiento {
  id: number;
  tipoMovimiento: string;
  monto: number;
  fecha: string;
  nombreCuenta: string;
  nombreCategoria: string;
  descripcion: string | null;
  etiquetaEstado: string;
}

export interface MovimientosPaginados {
  items: Movimiento[];
  totalResultados: number;
}

/** GET /alertas: las mismas que la tarjeta "Alertas" de la web. Las urgentes primero. */
export interface Alerta {
  id: number;
  tipoAlerta: string;
  nivelSeveridad: string;
  etiquetaSeveridad: string;
  mensaje: string;
  fechaReferencia: string | null;
  idDocumento: number | null;
  tipoCuenta: string | null;
  estadoAlerta: string;
  fechaGeneracion: string;
  ruta: string | null;
}

/**
 * Los mismos endpoints REST que la web. La app no calcula nada: muestra lo que devuelve el
 * backend, así el saldo y los movimientos son los mismos en las dos.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  caja(): Observable<CajaActual> {
    return this.http.get<CajaActual>(`${environment.apiUrl}/dashboard/caja`);
  }

  /** Los más recientes del mes en curso (el período por defecto del listado de la web), ingresos y egresos. */
  ultimosMovimientos(cantidad = 10): Observable<MovimientosPaginados> {
    return this.http.get<MovimientosPaginados>(`${environment.apiUrl}/movimientos`, {
      params: { pagina: 1, tamanoPagina: cantidad }
    });
  }

  alertas(): Observable<Alerta[]> {
    return this.http.get<Alerta[]>(`${environment.apiUrl}/alertas`);
  }
}

/** El mensaje que manda el backend ({ message }) o uno genérico según el tipo de falla. */
export function mensajeDeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'No pudimos conectar con el servidor. Revisa tu conexión.';
    }
    if (typeof error.error?.message === 'string') {
      return error.error.message;
    }
    if (error.status >= 500) {
      return 'Tuvimos un problema de nuestro lado. Inténtalo en unos minutos.';
    }
  }
  return 'Ocurrió un error inesperado. Inténtalo nuevamente.';
}
