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

export interface UsuarioEmpresa {
  idUsuario: number;
  nombre: string;
  email: string;
  rol: string;
  activo: boolean;
}

export interface Rol {
  id: number;
  nombreRol: string;
}

/**
 * passwordProvisional llega con valor solo cuando se creó una cuenta nueva. Es la única vez
 * que el sistema la muestra: no se guarda en claro, así que hay que copiarla en el momento.
 */
export interface UsuarioCreado {
  usuario: UsuarioEmpresa;
  passwordProvisional: string | null;
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

  listUsuarios(): Observable<UsuarioEmpresa[]> {
    return this.http.get<UsuarioEmpresa[]>('/api/empresa/usuarios').pipe(catchError(apiError));
  }

  listRoles(): Observable<Rol[]> {
    return this.http.get<Rol[]>('/api/empresa/roles').pipe(catchError(apiError));
  }

  agregarUsuario(payload: { nombre: string; email: string; idRol: number }): Observable<UsuarioCreado> {
    return this.http.post<UsuarioCreado>('/api/empresa/usuarios', payload).pipe(catchError(apiError));
  }

  cambiarRol(idUsuario: number, idRol: number): Observable<UsuarioEmpresa> {
    return this.http
      .put<UsuarioEmpresa>(`/api/empresa/usuarios/${idUsuario}/rol`, { idRol })
      .pipe(catchError(apiError));
  }

  cambiarEstado(idUsuario: number, activo: boolean): Observable<UsuarioEmpresa> {
    return this.http
      .patch<UsuarioEmpresa>(`/api/empresa/usuarios/${idUsuario}/estado`, { activo })
      .pipe(catchError(apiError));
  }
}
