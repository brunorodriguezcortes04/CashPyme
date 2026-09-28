import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

export interface Tercero {
  id: number;
  tipoTercero: string;
  nombreRazonSocial: string;
  rut: string | null;
  giro: string | null;
  email: string | null;
  telefono: string | null;
  direccion: string | null;
  activo: boolean;
}

export interface TerceroPayload {
  tipoTercero: string;
  nombreRazonSocial: string;
  rut?: string | null;
  giro?: string | null;
  email?: string | null;
  telefono?: string | null;
  direccion?: string | null;
}

@Injectable({ providedIn: 'root' })
export class TercerosService {
  private readonly http = inject(HttpClient);

  listTerceros(incluirInactivos = false): Observable<Tercero[]> {
    return this.http
      .get<Tercero[]>('/api/terceros', { params: { incluirInactivos } })
      .pipe(catchError(apiError));
  }

  crear(payload: TerceroPayload): Observable<Tercero> {
    return this.http.post<Tercero>('/api/terceros', payload).pipe(catchError(apiError));
  }

  actualizar(id: number, payload: TerceroPayload): Observable<Tercero> {
    return this.http.put<Tercero>(`/api/terceros/${id}`, payload).pipe(catchError(apiError));
  }

  cambiarEstado(id: number, activo: boolean): Observable<Tercero> {
    return this.http.patch<Tercero>(`/api/terceros/${id}/estado`, { activo }).pipe(catchError(apiError));
  }
}