import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map } from 'rxjs';
import { apiError } from './api-error';

export interface DiaProyeccion {
  fecha: string;
  ingresos: number;
  egresos: number;
  /** Saldo de caja proyectado al cierre del día. */
  saldo: number;
}

/** Un horizonte (30 o 60 días) ya resumido por el backend: el front no vuelve a sumar nada. */
export interface HorizonteProyeccion {
  horizonteDias: number;
  desde: string;
  hasta: string;
  /** Caja de hoy antes de lo que vence hoy. */
  saldoInicial: number;
  saldoFinal: number;
  totalIngresos: number;
  totalEgresos: number;
  saldoMinimo: number;
  fechaSaldoMinimo: string;
  /** null si el saldo no baja de cero en todo el horizonte. */
  primerDiaNegativo: string | null;
  dias: DiaProyeccion[];
}

/** Proyección guardada (ProyeccionFlujoCajaService en el backend). */
export interface Proyeccion {
  fechaGeneracion: string;
  modeloUsado: string;
  etiquetaModelo: string;
  horizontes: HorizonteProyeccion[];
}

@Injectable({ providedIn: 'root' })
export class ProyeccionService {
  private readonly http = inject(HttpClient);

  /** La última que se generó, tal como se guardó; null si la empresa todavía no tiene ninguna (204). */
  getUltima(): Observable<Proyeccion | null> {
    return this.http
      .get<Proyeccion | null>('/api/proyeccion/ultima')
      .pipe(map((p) => p ?? null), catchError(apiError));
  }

  /** Genera una nueva con los datos de ahora. El backend también actualiza la alerta de caja negativa. */
  generar(): Observable<Proyeccion> {
    return this.http.post<Proyeccion>('/api/proyeccion', {}).pipe(catchError(apiError));
  }
}
