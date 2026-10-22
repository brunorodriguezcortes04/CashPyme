import { DatePipe, DecimalPipe, TitleCasePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { mensajeDeError } from '../../core/api-error';
import { CajaActual, DashboardService, Indicadores, Variacion } from '../../core/dashboard.service';

// Geometría del gráfico (unidades del viewBox; el SVG se estira al ancho de la tarjeta).
const CHART_WIDTH = 640;
const CHART_HEIGHT = 220;
const CHART_PAD_TOP = 12;
/** Alto mínimo de una barra con monto, para que un día chico no desaparezca. */
const BARRA_MINIMA = 2;

/** Días del mes que se rotulan en el eje. El último se agrega aparte (28, 30 o 31). */
const DIAS_EJE = [1, 8, 15, 22];

const pesos = new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 });

interface BarraDia {
  fecha: string;
  dia: number;
  ingresos: number;
  egresos: number;
  /** Franja completa del día: es la zona que reacciona al mouse. */
  franjaX: number;
  franjaAncho: number;
  ingresoX: number;
  egresoX: number;
  ancho: number;
  ingresoY: number;
  ingresoAlto: number;
  egresoY: number;
  egresoAlto: number;
}

@Component({
  selector: 'app-caja',
  imports: [DatePipe, DecimalPipe, TitleCasePipe],
  templateUrl: './caja.html',
  styleUrl: './caja.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Caja {
  private readonly dashboardService = inject(DashboardService);

  protected readonly caja = signal<CajaActual | null>(null);
  /** Variación contra el mes anterior y ranking de categorías del mismo mes que la caja. */
  protected readonly indicadores = signal<Indicadores | null>(null);
  protected readonly errorIndicadores = signal<string | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  /** Mes mostrado ("2026-10"). Vacío al entrar: el backend usa el mes en curso de la empresa. */
  protected readonly mes = signal('');
  /** Mes en curso según el backend: no se puede avanzar más allá. */
  private readonly mesActual = signal('');
  /** Día bajo el mouse en el gráfico (índice en barras), o null. */
  protected readonly diaActivo = signal<number | null>(null);

  protected readonly chartWidth = CHART_WIDTH;
  protected readonly chartHeight = CHART_HEIGHT;

  protected readonly esMesActual = computed(() => this.mes() !== '' && this.mes() >= this.mesActual());

  /**
   * Una franja por cada día del período, tenga o no movimientos. Solo es dibujo: los montos
   * vienen tal cual del backend, acá no se suma nada.
   */
  protected readonly barras = computed<BarraDia[]>(() => {
    const c = this.caja();
    if (!c) {
      return [];
    }

    const porFecha = new Map(c.flujoDiario.map((d) => [d.fecha, d]));
    const prefijo = c.desde.slice(0, 8);
    const dias = Number(c.hasta.slice(8, 10));
    const maximo = Math.max(1, ...c.flujoDiario.flatMap((d) => [d.ingresos, d.egresos]));

    const franja = CHART_WIDTH / dias;
    const ancho = franja * 0.32;
    const altoUtil = CHART_HEIGHT - CHART_PAD_TOP;
    const alto = (monto: number) => (monto > 0 ? Math.max(BARRA_MINIMA, (monto / maximo) * altoUtil) : 0);

    return Array.from({ length: dias }, (_, i) => {
      const dia = i + 1;
      const fecha = `${prefijo}${String(dia).padStart(2, '0')}`;
      const ingresos = porFecha.get(fecha)?.ingresos ?? 0;
      const egresos = porFecha.get(fecha)?.egresos ?? 0;
      const ingresoAlto = alto(ingresos);
      const egresoAlto = alto(egresos);
      const ingresoX = i * franja + franja * 0.15;

      return {
        fecha,
        dia,
        ingresos,
        egresos,
        franjaX: i * franja,
        franjaAncho: franja,
        ingresoX,
        egresoX: ingresoX + ancho + franja * 0.06,
        ancho,
        ingresoY: CHART_HEIGHT - ingresoAlto,
        ingresoAlto,
        egresoY: CHART_HEIGHT - egresoAlto,
        egresoAlto
      };
    });
  });

  /** Rótulos del eje, en % del ancho, centrados en su día. */
  protected readonly eje = computed(() => {
    const total = this.barras().length;
    if (total === 0) {
      return [];
    }
    return [...DIAS_EJE.filter((d) => d < total - 2), total].map((dia) => ({
      dia,
      left: ((dia - 0.5) / total) * 100
    }));
  });

  protected readonly detalleActivo = computed(() => {
    const i = this.diaActivo();
    return i === null ? null : (this.barras()[i] ?? null);
  });

  /** Barras de saldo por cuenta: el ancho es relativo a la cuenta con más saldo (en valor absoluto). */
  protected readonly barrasCuentas = computed(() => {
    const cuentas = this.caja()?.cuentas ?? [];
    const maximo = Math.max(1, ...cuentas.map((c) => Math.abs(c.saldoActual)));
    return cuentas.map((c) => ({ ...c, porcentaje: (Math.abs(c.saldoActual) / maximo) * 100 }));
  });

  protected readonly descripcionGrafico = computed(() => {
    const c = this.caja();
    return c
      ? `Ingresos y egresos por día del ${c.desde} al ${c.hasta}. Total de ingresos ${pesos.format(c.ingresos)}, total de egresos ${pesos.format(c.egresos)}. El detalle por día está en la tabla de abajo.`
      : '';
  });

  /** Las tres comparaciones, con su título: los números y si son favorables vienen del backend. */
  protected readonly variaciones = computed<{ titulo: string; v: Variacion }[]>(() => {
    const i = this.indicadores();
    return i
      ? [
          { titulo: 'Ingresos', v: i.ingresos },
          { titulo: 'Egresos', v: i.egresos },
          { titulo: 'Flujo neto', v: i.flujoNeto }
        ]
      : [];
  });

  /** Las dos listas del ranking, en el orden en que se muestran. */
  protected readonly rankings = computed(() => {
    const i = this.indicadores();
    return i
      ? [
          { titulo: 'En qué se fue la plata', items: i.topEgresos, clase: 'egreso' },
          { titulo: 'De dónde entró', items: i.topIngresos, clase: 'ingreso' }
        ]
      : [];
  });

  constructor() {
    this.cargar();
  }

  /** Mueve el período un mes hacia atrás (-1) o adelante (+1). */
  protected moverMes(delta: number): void {
    const [anio, mes] = this.mes().split('-').map(Number);
    const meses = anio * 12 + (mes - 1) + delta;
    this.mes.set(`${Math.floor(meses / 12)}-${String((meses % 12) + 1).padStart(2, '0')}`);
    this.cargar();
  }

  protected cargar(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.diaActivo.set(null);

    const [anio, mes] = this.mes() ? this.mes().split('-').map(Number) : [undefined, undefined];

    this.errorIndicadores.set(null);
    this.dashboardService.getIndicadores(anio, mes).subscribe({
      next: (indicadores) => this.indicadores.set(indicadores),
      error: (error: unknown) => {
        this.indicadores.set(null);
        this.errorIndicadores.set(mensajeDeError(error));
      }
    });

    this.dashboardService.getCajaActual(anio, mes).subscribe({
      next: (caja) => {
        const mesRespuesta = caja.desde.slice(0, 7);
        if (!this.mesActual()) {
          // La primera carga es sin período: lo que devuelve es el mes en curso de la empresa.
          this.mesActual.set(mesRespuesta);
        }
        this.caja.set(caja);
        this.mes.set(mesRespuesta);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }
}
