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

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getSaldoNeto(): Observable<SaldoNeto> {
    return this.http.get<SaldoNeto>('/api/dashboard/saldo-neto').pipe(catchError(apiError));
  }
}
