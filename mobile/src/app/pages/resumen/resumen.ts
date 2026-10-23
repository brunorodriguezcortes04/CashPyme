import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  IonButton,
  IonButtons,
  IonContent,
  IonHeader,
  IonItem,
  IonLabel,
  IonList,
  IonListHeader,
  IonNote,
  IonRefresher,
  IonRefresherContent,
  IonSkeletonText,
  IonTitle,
  IonToolbar,
  RefresherCustomEvent
} from '@ionic/angular';
import { firstValueFrom, forkJoin } from 'rxjs';
import { ApiService, CajaActual, Movimiento, mensajeDeError } from '../../core/api.service';
import { ClpPipe } from '../../core/clp.pipe';
import { SesionService } from '../../core/sesion.service';

/**
 * Vista principal: el saldo total y los últimos movimientos, de los mismos endpoints que la
 * web (/dashboard/caja y /movimientos). Tirar hacia abajo vuelve a pedirlos.
 */
@Component({
  selector: 'app-resumen',
  imports: [
    DatePipe, ClpPipe, RouterLink,
    IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonContent, IonRefresher, IonRefresherContent,
    IonList, IonListHeader, IonItem, IonLabel, IonNote, IonSkeletonText
  ],
  templateUrl: './resumen.html',
  styleUrl: './resumen.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Resumen {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly sesion = inject(SesionService);

  protected readonly caja = signal<CajaActual | null>(null);
  protected readonly movimientos = signal<Movimiento[]>([]);
  protected readonly cargando = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.error.set(null);
    try {
      const { caja, movimientos } = await firstValueFrom(
        forkJoin({ caja: this.api.caja(), movimientos: this.api.ultimosMovimientos() })
      );
      this.caja.set(caja);
      this.movimientos.set(movimientos.items);
    } catch (e) {
      this.error.set(mensajeDeError(e));
    } finally {
      this.cargando.set(false);
    }
  }

  protected async refrescar(evento: RefresherCustomEvent): Promise<void> {
    await this.cargar();
    await evento.target.complete();
  }

  protected async salir(): Promise<void> {
    await this.sesion.cerrar();
    await this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
