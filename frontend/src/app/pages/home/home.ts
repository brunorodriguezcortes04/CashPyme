import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

const PERIODS = [30, 60, 90] as const;
type Period = (typeof PERIODS)[number];

const TODAY_BALANCE = 4_820_000;
const HISTORY_DAYS = 30;
const CHART_WIDTH = 640;
const CHART_HEIGHT = 240;
const CHART_PAD_X = 8;
const CHART_PAD_TOP = 16;
const CHART_PAD_BOTTOM = 12;

const currency = new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat('es-CL', { style: 'percent', maximumFractionDigits: 0 });

function balanceAt(day: number): number {
  const value = day <= 0
    ? TODAY_BALANCE + 11_000 * day + 110_000 * Math.sin(day / 7)
    : TODAY_BALANCE + 14_000 * day + 160_000 * Math.sin(day / 11);
  return Math.round(value / 10_000) * 10_000;
}

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Home {
  private readonly authService = inject(AuthService);

  protected readonly periods = PERIODS;
  protected readonly period = signal<Period>(60);
  protected readonly session = this.authService.session;

  protected readonly summary = computed(() => {
    const days = this.period();
    const end = balanceAt(days);
    const difference = end - TODAY_BALANCE;
    const sign = difference >= 0 ? '+' : '−';
    return {
      days,
      today: currency.format(TODAY_BALANCE),
      end: currency.format(end),
      change: `${sign}${currency.format(Math.abs(difference))} (${sign}${percent.format(Math.abs(difference) / TODAY_BALANCE)})`,
      rises: difference >= 0
    };
  });

  protected readonly chart = computed(() => {
    const days = this.period();
    const span = HISTORY_DAYS + days;
    const offsets = Array.from({ length: span + 1 }, (_, index) => index - HISTORY_DAYS);
    const values = offsets.map(balanceAt);
    const low = Math.min(...values);
    const high = Math.max(...values);
    const margin = (high - low) * 0.08;
    const x = (day: number) => CHART_PAD_X + ((day + HISTORY_DAYS) / span) * (CHART_WIDTH - CHART_PAD_X * 2);
    const y = (value: number) =>
      CHART_PAD_TOP + (1 - (value - (low - margin)) / (high - low + margin * 2)) * (CHART_HEIGHT - CHART_PAD_TOP - CHART_PAD_BOTTOM);
    const points = offsets.map((day, index) => ({ day, x: x(day), y: y(values[index]) }));
    const line = (from: number, to: number) =>
      points
        .filter((point) => point.day >= from && point.day <= to)
        .map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`)
        .join(' ');
    const today = points.find((point) => point.day === 0)!;
    const end = points[points.length - 1];
    const first = points[0];

    return {
      width: CHART_WIDTH,
      height: CHART_HEIGHT,
      history: line(-HISTORY_DAYS, 0),
      forecast: line(0, days),
      area: `${line(-HISTORY_DAYS, days)} L${end.x.toFixed(1)} ${CHART_HEIGHT} L${first.x.toFixed(1)} ${CHART_HEIGHT} Z`,
      today,
      end,
      todayLeft: (today.x / CHART_WIDTH) * 100,
      description: `Gráfico de ejemplo: hoy la caja es ${currency.format(TODAY_BALANCE)} y en ${days} días sería ${currency.format(balanceAt(days))}.`
    };
  });

  protected selectPeriod(days: Period) {
    this.period.set(days);
  }

  protected logout() {
    this.authService.logout();
  }
}
