import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError } from 'rxjs';
import { apiError } from './api-error';

export interface Empresa {
  id: number;
  razonSocial: string;
  rut: string | null;
  giro: string | null;
  direccion: string | null;
  telefono: string | null;
  emailContacto: string | null;
  zonaHoraria: string;
  diasAvisoVencimiento: number;
  umbralSaldoBajo: number;
}

export interface EmpresaPayload {
  razonSocial: string;
  rut?: string | null;
  giro?: string | null;
  direccion?: string | null;
  telefono?: string | null;
  emailContacto?: string | null;
  diasAvisoVencimiento: number;
  umbralSaldoBajo: number;
}

@Injectable({ providedIn: 'root' })
export class EmpresaService {
  private readonly http = inject(HttpClient);

  obtener(): Observable<Empresa> {
    return this.http.get<Empresa>('/api/empresa').pipe(catchError(apiError));
  }

  actualizar(payload: EmpresaPayload): Observable<Empresa> {
    return this.http.put<Empresa>('/api/empresa', payload).pipe(catchError(apiError));
  }
}
