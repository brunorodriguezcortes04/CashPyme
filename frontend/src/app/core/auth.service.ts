import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, tap } from 'rxjs';
import { apiError } from './api-error';
import { PantallasService } from './pantallas.service';

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
  private readonly pantallasService = inject(PantallasService);

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

  /**
   * No devuelve token nuevo ni cierra la sesión: el JWT sigue siendo válido porque va
   * firmado con el id del usuario, no con la contraseña.
   */
  cambiarPassword(passwordActual: string, passwordNueva: string): Observable<void> {
    return this.http
      .put<void>('/api/auth/password', { passwordActual, passwordNueva })
      .pipe(catchError(apiError));
  }

  /**
   * El backend responde igual exista o no el correo (para no revelar qué cuentas hay),
   * así que acá no hay nada que interpretar: si no hubo error, se muestra el mismo aviso.
   */
  olvidePassword(email: string): Observable<void> {
    return this.http.post<void>('/api/auth/olvide-password', { email }).pipe(catchError(apiError));
  }

  restablecerPassword(token: string, passwordNueva: string): Observable<void> {
    return this.http
      .post<void>('/api/auth/restablecer-password', { token, passwordNueva })
      .pipe(catchError(apiError));
  }

  verificarEmail(token: string): Observable<void> {
    return this.http.post<void>('/api/auth/verificar-email', { token }).pipe(catchError(apiError));
  }

  reenviarVerificacion(): Observable<void> {
    return this.http.post<void>('/api/auth/reenviar-verificacion', {}).pipe(catchError(apiError));
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY);
  }

  /**
   * Limpia también las pantallas cacheadas. Va acá y no en quien llama porque el menú de
   * una persona no puede sobrevivirle a su sesión: si se olvidaba, la siguiente que entrara
   * en la misma pestaña veía el menú de la anterior (un Operador con las pantallas de un
   * Administrador). Antes dependía de acordarse de llamarlo desde el shell.
   */
  logout(): void {
    localStorage.removeItem(SESSION_KEY);
    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(SESSION_KEY);
    sessionStorage.removeItem(TOKEN_KEY);
    this.session.set(null);
    this.isAuthenticated.set(false);
    this.pantallasService.limpiarCache();
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
