import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { mensajeDeError } from '../../../core/api-error';
import { HorizonteProyeccion, Proyeccion, ProyeccionService } from '../../../core/proyeccion.service';

// Geometría del gráfico (unidades del viewBox; el SVG se estira al ancho de la tarjeta).
const CHART_WIDTH = 640;
const CHART_HEIGHT = 200;
const CHART_PAD = 10;

/** Cuántos rótulos lleva el eje de fechas, contando el primero y el último. */
const MARCAS_EJE = 5;

const pesos = new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 });

interface FranjaDia {
  fecha: string;
  ingresos: number;
  egresos: number;
  saldo: number;
  x: number;
  ancho: number;
}

/**
 * Tarjeta de proyección de caja del Resumen. Muestra la última proyección guardada en el backend
 * (no la recalcula al abrir) y deja generar una nueva. Todos los montos vienen del backend: acá
 * solo se dibujan.
 */
@Component({
  selector: 'app-proyeccion-caja',
  imports: [DatePipe, DecimalPipe],
  templateUrl: './proyeccion-caja.html',
  styleUrl: './proyeccion-caja.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProyeccionCaja {
  private readonly proyeccionService = inject(ProyeccionService);

  /** Avisa que se generó una proyección nueva: puede haber creado o cerrado la alerta de caja negativa. */
  readonly generada = output<void>();

  protected readonly proyeccion = signal<Proyeccion | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly generando = signal(false);
  protected readonly errorGenerar = signal<string | null>(null);
  protected readonly horizonteDias = signal(30);
  /** Día bajo el mouse (índice en franjas), o null. */
  protected readonly diaActivo = signal<number | null>(null);

  protected readonly chartWidth = CHART_WIDTH;
  protected readonly chartHeight = CHART_HEIGHT;

  /** Los horizontes que trae la proyección, para las pestañas (30 y 60 días). */
  protected readonly opciones = computed(() => this.proyeccion()?.horizontes.map((h) => h.horizonteDias) ?? []);

  protected readonly horizonte = computed<HorizonteProyeccion | null>(() => {
    const horizontes = this.proyeccion()?.horizontes ?? [];
    return horizontes.find((h) => h.horizonteDias === this.horizonteDias()) ?? horizontes[0] ?? null;
  });

  /** Escala vertical: siempre incluye el cero, para que se vea cuándo el saldo lo cruza. */
  private readonly escala = computed(() => {
    const saldos = this.horizonte()?.dias.map((d) => d.saldo) ?? [0];
    const min = Math.min(0, ...saldos);
    const max = Math.max(0, ...saldos);
    const rango = max - min || 1;
    const altoUtil = CHART_HEIGHT - CHART_PAD * 2;
    return (monto: number) => CHART_PAD + ((max - monto) / rango) * altoUtil;
  });

  /** Una franja por día: el saldo vale lo mismo todo el día y salta cuando algo vence. */
  protected readonly franjas = computed<FranjaDia[]>(() => {
    const dias = this.horizonte()?.dias ?? [];
    const ancho = CHART_WIDTH / Math.max(1, dias.length);
    return dias.map((d, i) => ({ ...d, x: i * ancho, ancho }));
  });

  /** Altura del cero en el gráfico. */
  protected readonly yCero = computed(() => this.escala()(0));

  /** Línea escalonada del saldo, día por día. */
  protected readonly linea = computed(() => {
    const y = this.escala();
    return this.franjas()
      .map((f, i) => `${i === 0 ? 'M' : 'L'}${f.x},${y(f.saldo)} H${f.x + f.ancho}`)
      .join(' ');
  });

  /** La misma línea cerrada contra el cero: se pinta azul sobre el cero y roja bajo él. */
  protected readonly area = computed(() => {
    const linea = this.linea();
    return linea ? `${linea} V${this.yCero()} H0 Z` : '';
  });

  protected readonly cruzaCero = computed(() => this.horizonte()?.primerDiaNegativo != null);

  protected readonly eje = computed(() => {
    const franjas = this.franjas();
    if (franjas.length === 0) {
      return [];
    }
    const ultimo = franjas.length - 1;
    const indices = [...new Set(Array.from({ length: MARCAS_EJE }, (_, k) => Math.round((k * ultimo) / (MARCAS_EJE - 1))))];
    return indices.map((i) => ({ fecha: franjas[i].fecha, left: ((i + 0.5) / franjas.length) * 100 }));
  });

  protected readonly detalleActivo = computed(() => {
    const i = this.diaActivo();
    return i === null ? null : (this.franjas()[i] ?? null);
  });

  /** Para la tabla de detalle: solo los días en que algo entra o sale. */
  protected readonly diasConMovimiento = computed(
    () => this.horizonte()?.dias.filter((d) => d.ingresos > 0 || d.egresos > 0) ?? []
  );

  protected readonly descripcionGrafico = computed(() => {
    const h = this.horizonte();
    if (!h) {
      return '';
    }
    const negativo = h.primerDiaNegativo ? ` Queda en negativo desde el ${h.primerDiaNegativo}.` : '';
    return `Saldo de caja proyectado por día del ${h.desde} al ${h.hasta}. Parte en ${pesos.format(h.saldoInicial)} y termina en ${pesos.format(h.saldoFinal)}; el punto más bajo es ${pesos.format(h.saldoMinimo)} el ${h.fechaSaldoMinimo}.${negativo} El detalle está en la tabla de abajo.`;
  });

  constructor() {
    this.cargar();
  }

  protected elegirHorizonte(dias: number): void {
    this.horizonteDias.set(dias);
    this.diaActivo.set(null);
  }

  protected cargar(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.proyeccionService.getUltima().subscribe({
      next: (proyeccion) => {
        this.proyeccion.set(proyeccion);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }

  protected generar(): void {
    this.generando.set(true);
    this.errorGenerar.set(null);

    this.proyeccionService.generar().subscribe({
      next: (proyeccion) => {
        this.proyeccion.set(proyeccion);
        this.diaActivo.set(null);
        this.generando.set(false);
        this.generada.emit();
      },
      error: (error: unknown) => {
        // Si falla, queda a la vista la anterior: sigue siendo la última guardada.
        this.errorGenerar.set(mensajeDeError(error));
        this.generando.set(false);
      }
    });
  }
}
