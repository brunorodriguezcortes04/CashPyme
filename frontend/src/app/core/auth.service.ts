import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, tap } from 'rxjs';
import { apiError } from './api-error';

export interface RegisterPayload {
  businessName: string;
  email: string;
  password: string;
}

export interface LoginPayload {
  email: string;
  password: string;
  rememberMe: boolean;
}

interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  businessName: string;
  email: string;
  rol: string;
}

export interface Session {
  businessName: string;
  email: string;
  rol: string;
}

export interface EmpresaMembresia {
  id: number;
  razonSocial: string;
  rol: string;
  esActiva: boolean;
}

const SESSION_KEY = 'cashpyme.session';
const TOKEN_KEY = 'cashpyme.token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly session = signal<Session | null>(this.readStoredSession());
  readonly isAuthenticated = signal(this.session() !== null);

  listEmpresas(): Observable<EmpresaMembresia[]> {
    return this.http.get<EmpresaMembresia[]>('/api/empresas').pipe(catchError(apiError));
  }

  cambiarEmpresa(idEmpresa: number): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`/api/empresas/${idEmpresa}/activar`, {}).pipe(
      tap((response) => this.storeSession(response, localStorage.getItem(TOKEN_KEY) !== null)),
      catchError(apiError)
    );
  }

  register(payload: RegisterPayload): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', payload).pipe(
      tap((response) => this.storeSession(response, true)),
      catchError(apiError)
    );
  }

  login(payload: LoginPayload): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', payload).pipe(
      tap((response) => this.storeSession(response, payload.rememberMe)),
      catchError(apiError)
    );
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY);
  }

  logout(): void {
    localStorage.removeItem(SESSION_KEY);
    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(SESSION_KEY);
    sessionStorage.removeItem(TOKEN_KEY);
    this.session.set(null);
    this.isAuthenticated.set(false);
  }

  private storeSession(response: AuthResponse, persist: boolean): void {
    const storage = persist ? localStorage : sessionStorage;
    const session: Session = {
      businessName: response.businessName,
      email: response.email,
      rol: response.rol
    };
    storage.setItem(SESSION_KEY, JSON.stringify(session));
    storage.setItem(TOKEN_KEY, response.token);
    this.session.set(session);
    this.isAuthenticated.set(true);
  }

  private readStoredSession(): Session | null {
    const raw = localStorage.getItem(SESSION_KEY) ?? sessionStorage.getItem(SESSION_KEY);
    if (!raw) {
      return null;
    }

    try {
      const session = JSON.parse(raw) as Session;
      // Sesión guardada antes de que existiera el rol: se descarta para forzar login.
      return typeof session.rol === 'string' ? session : null;
    } catch {
      return null;
    }
  }
}
