import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Preferences } from '@capacitor/preferences';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

/** Lo que responde POST /auth/login: el mismo endpoint que usa la web. */
interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  businessName: string;
  email: string;
  rol: string;
}

export interface Sesion {
  token: string;
  empresa: string;
  email: string;
  rol: string;
}

const CLAVE = 'cashpyme.sesion';

/**
 * Sesión de la app. El token es el mismo JWT que usa la web; se guarda con Capacitor Preferences
 * (almacenamiento nativo en el teléfono, localStorage en el navegador) para no pedir login cada
 * vez que se abre la app.
 */
@Injectable({ providedIn: 'root' })
export class SesionService {
  private readonly http = inject(HttpClient);

  readonly sesion = signal<Sesion | null>(null);
  private carga?: Promise<Sesion | null>;

  /** Lee la sesión guardada una sola vez; los guards la esperan antes de decidir. */
  cargar(): Promise<Sesion | null> {
    this.carga ??= Preferences.get({ key: CLAVE }).then(({ value }) => {
      let guardada: Sesion | null = null;
      try {
        guardada = value ? (JSON.parse(value) as Sesion) : null;
      } catch {
        guardada = null;
      }
      this.sesion.set(guardada?.token ? guardada : null);
      return this.sesion();
    });
    return this.carga;
  }

  async iniciar(email: string, password: string): Promise<void> {
    const r = await firstValueFrom(
      this.http.post<AuthResponse>(`${environment.apiUrl}/auth/login`, { email, password, rememberMe: true })
    );
    const sesion: Sesion = { token: r.token, empresa: r.businessName, email: r.email, rol: r.rol };
    await Preferences.set({ key: CLAVE, value: JSON.stringify(sesion) });
    this.sesion.set(sesion);
    this.carga = Promise.resolve(sesion);
  }

  async cerrar(): Promise<void> {
    await Preferences.remove({ key: CLAVE });
    this.sesion.set(null);
    this.carga = Promise.resolve(null);
  }
}
