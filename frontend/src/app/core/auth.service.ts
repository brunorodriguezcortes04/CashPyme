import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, tap, throwError } from 'rxjs';

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
}

export interface Session {
  businessName: string;
  email: string;
}

const SESSION_KEY = 'cashpyme.session';
const TOKEN_KEY = 'cashpyme.token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly session = signal<Session | null>(this.readStoredSession());
  readonly isAuthenticated = signal(this.session() !== null);

  register(payload: RegisterPayload): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', payload).pipe(
      tap((response) => this.storeSession(response, true)),
      catchError((error: HttpErrorResponse) => throwError(() => new Error(this.messageFor(error))))
    );
  }

  login(payload: LoginPayload): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', payload).pipe(
      tap((response) => this.storeSession(response, payload.rememberMe)),
      catchError((error: HttpErrorResponse) => throwError(() => new Error(this.messageFor(error))))
    );
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
    const session: Session = { businessName: response.businessName, email: response.email };
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
      return JSON.parse(raw) as Session;
    } catch {
      return null;
    }
  }

  private messageFor(error: HttpErrorResponse): string {
    if (error.status === 0) {
      return 'No pudimos conectar con el servidor. Verifica tu conexión e inténtalo de nuevo.';
    }

    if (error.status === 409) {
      return 'Ya existe una cuenta con este correo electrónico.';
    }

    if (error.status === 401) {
      return 'Correo o contraseña incorrectos.';
    }

    return error.error?.message ?? 'Ocurrió un error inesperado. Inténtalo nuevamente.';
  }
}
