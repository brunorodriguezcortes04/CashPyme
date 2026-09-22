import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

export interface Cuenta {
  id: number;
  nombreCuenta: string;
  tipoCuenta: string;
  banco: string | null;
  numeroCuenta: string | null;
  saldoInicial: number;
  saldoActual: number;
  activo: boolean;
}

export interface CuentaPayload {
  nombreCuenta: string;
  tipoCuenta: string;
  banco?: string | null;
  numeroCuenta?: string | null;
  saldoInicial: number;
}

@Injectable({ providedIn: 'root' })
export class CuentasService {
  private readonly http = inject(HttpClient);

  listCuentas(incluirInactivas = false): Observable<Cuenta[]> {
    return this.http
      .get<Cuenta[]>('/api/cuentas', { params: { incluirInactivas } })
      .pipe(catchError(apiError));
  }

  crear(payload: CuentaPayload): Observable<Cuenta> {
    return this.http.post<Cuenta>('/api/cuentas', payload).pipe(catchError(apiError));
  }

  actualizar(id: number, payload: CuentaPayload): Observable<Cuenta> {
    return this.http.put<Cuenta>(`/api/cuentas/${id}`, payload).pipe(catchError(apiError));
  }

  cambiarEstado(id: number, activo: boolean): Observable<Cuenta> {
    return this.http.patch<Cuenta>(`/api/cuentas/${id}/estado`, { activo }).pipe(catchError(apiError));
  }
}
