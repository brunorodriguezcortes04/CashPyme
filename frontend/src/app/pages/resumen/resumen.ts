import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Alerta, AlertasService } from '../../core/alertas.service';
import { mensajeDeError } from '../../core/api-error';
import { DashboardService, SaldoNeto } from '../../core/dashboard.service';

@Component({
  selector: 'app-resumen',
  imports: [DecimalPipe, RouterLink],
  templateUrl: './resumen.html',
  styleUrl: './resumen.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Resumen {
  private readonly dashboardService = inject(DashboardService);
  private readonly alertasService = inject(AlertasService);

  protected readonly saldo = signal<SaldoNeto | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly alertas = signal<Alerta[]>([]);
  protected readonly cargandoAlertas = signal(true);
  protected readonly errorAlertas = signal<string | null>(null);
  /** Alerta que se está marcando como leída, para deshabilitar solo su botón. */
  protected readonly marcandoId = signal<number | null>(null);

  /** Un neto negativo es el aviso que importa: aunque cobres todo, no alcanza para pagar. */
  protected readonly esNegativo = computed(() => (this.saldo()?.saldoNeto ?? 0) < 0);

  /** Sin nada pendiente, el neto es el saldo de caja y no vale la pena mostrar el desglose. */
  protected readonly sinPendientes = computed(() => {
    const s = this.saldo();
    return s !== null && s.porCobrar === 0 && s.porPagar === 0;
  });

  constructor() {
    this.cargar();
    this.cargarAlertas();
  }

  protected cargar(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.dashboardService.getSaldoNeto().subscribe({
      next: (saldo) => {
        this.saldo.set(saldo);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }

  protected cargarAlertas(): void {
    this.cargandoAlertas.set(true);
    this.errorAlertas.set(null);

    this.alertasService.listPendientes().subscribe({
      next: (alertas) => {
        this.alertas.set(alertas);
        this.cargandoAlertas.set(false);
      },
      error: (error: unknown) => {
        this.errorAlertas.set(mensajeDeError(error));
        this.cargandoAlertas.set(false);
      }
    });
  }

  /**
   * Leerla la saca de la lista para toda la empresa, no solo para quien hace clic: la tabla
   * guarda un solo estado por alerta. Mientras el motivo siga vigente no vuelve a aparecer.
   */
  protected marcarLeida(alerta: Alerta): void {
    this.marcandoId.set(alerta.id);
    this.errorAlertas.set(null);

    this.alertasService.marcarLeida(alerta.id).subscribe({
      next: () => {
        this.alertas.update((actuales) => actuales.filter((a) => a.id !== alerta.id));
        this.marcandoId.set(null);
      },
      error: (error: unknown) => {
        this.errorAlertas.set(mensajeDeError(error));
        this.marcandoId.set(null);
      }
    });
  }
}
