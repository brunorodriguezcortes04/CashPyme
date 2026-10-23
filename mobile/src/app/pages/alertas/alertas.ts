import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  IonBackButton, IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList,
  IonRefresher, IonRefresherContent, IonSkeletonText, IonTitle, IonToolbar, RefresherCustomEvent
} from '@ionic/angular';
import { firstValueFrom } from 'rxjs';
import { Alerta, ApiService, mensajeDeError } from '../../core/api.service';

/**
 * Alertas pendientes de la empresa, del mismo GET /alertas que la web. El orden y la etiqueta
 * de severidad los decide el backend: aquí solo se muestran.
 */
@Component({
  selector: 'app-alertas',
  imports: [
    IonHeader, IonToolbar, IonTitle, IonButtons, IonBackButton, IonButton, IonContent,
    IonRefresher, IonRefresherContent, IonList, IonItem, IonLabel, IonSkeletonText
  ],
  templateUrl: './alertas.html',
  styleUrl: './alertas.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Alertas {
  private readonly api = inject(ApiService);

  protected readonly alertas = signal<Alerta[]>([]);
  protected readonly cargando = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.cargar();
  }

  protected async cargar(): Promise<void> {
    this.error.set(null);
    try {
      this.alertas.set(await firstValueFrom(this.api.alertas()));
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
}