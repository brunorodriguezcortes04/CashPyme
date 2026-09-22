import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, shareReplay, tap } from 'rxjs';

export interface Pantalla {
  codigo: string;
  nombre: string;
  ruta: string;
  orden: number;
}

@Injectable({ providedIn: 'root' })
export class PantallasService {
  private readonly http = inject(HttpClient);

  // El menú y el guard piden lo mismo en cada navegación: se resuelve una vez por sesión.
  private pantallas$?: Observable<Pantalla[]>;
  private ultimas: Pantalla[] = [];

  listPantallas(): Observable<Pantalla[]> {
    this.pantallas$ ??= this.http.get<Pantalla[]>('/api/pantallas').pipe(
      tap((pantallas) => (this.ultimas = pantallas)),
      catchError(() => of([])),
      shareReplay({ bufferSize: 1, refCount: false })
    );

    return this.pantallas$;
  }

  rutaInicial(): string | null {
    return this.ultimas[0]?.ruta ?? null;
  }

  /** Se llama al cambiar de empresa o cerrar sesión: el rol cambia y con él las pantallas. */
  limpiarCache(): void {
    this.pantallas$ = undefined;
    this.ultimas = [];
  }
}
