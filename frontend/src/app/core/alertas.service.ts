import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

/**
 * Aviso generado por el job de alertas del backend (AlertasService). El front no las crea ni las
 * resuelve: solo las muestra y las marca como leídas.
 */
export interface Alerta {
  id: number;
  tipoAlerta: string;
  /** Se usa solo para el estilo; el texto que se muestra es etiquetaSeveridad. */
  nivelSeveridad: string;
  /** "Urgente", "Atención" o "Aviso": lo decide el backend. */
  etiquetaSeveridad: string;
  mensaje: string;
  fechaReferencia: string | null;
  idDocumento: number | null;
  /** 'por_cobrar' o 'por_pagar' si la alerta es de un documento; null si es de saldo. */
  tipoCuenta: string | null;
  estadoAlerta: string;
  fechaGeneracion: string;
  /** Ruta del backoffice donde se resuelve (p. ej. 'cuentas-por-cobrar'); null si no hay una. */
  ruta: string | null;
}

@Injectable({ providedIn: 'root' })
export class AlertasService {
  private readonly http = inject(HttpClient);

  /** Alertas sin leer de la empresa, las críticas primero. */
  listPendientes(): Observable<Alerta[]> {
    return this.http.get<Alerta[]>('/api/alertas').pipe(catchError(apiError));
  }

  marcarLeida(id: number): Observable<void> {
    return this.http.post<void>(`/api/alertas/${id}/leida`, {}).pipe(catchError(apiError));
  }
}
