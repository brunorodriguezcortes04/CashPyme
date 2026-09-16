import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RevealOnScrollDirective } from './reveal-on-scroll.directive';
import { ParallaxDirective } from './parallax.directive';

@Component({
  selector: 'app-home',
  imports: [RouterLink, RevealOnScrollDirective, ParallaxDirective],
  templateUrl: './home.html',
  styleUrl: './home.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Home {
  protected readonly isMenuOpen = signal(false);
  protected readonly projectionDays = signal(60);

  protected readonly projections = {
    30: { amount: '$5.240.000', change: '+ 8,4%', points: '0,68 36,60 72,64 108,49 144,55 180,40 216,44 252,27' },
    60: { amount: '$6.760.000', change: '+ 14,8%', points: '0,68 36,60 72,64 108,49 144,55 180,40 216,44 252,27 288,18' },
    90: { amount: '$8.120.000', change: '+ 21,6%', points: '0,68 36,60 72,64 108,49 144,55 180,40 216,44 252,27 288,18 324,10' }
  } as const;

  protected readonly features = [
    { icon: 'chart', title: 'Saldo en tiempo real', text: 'Conoce el estado exacto de tu caja en todo momento.' },
    { icon: 'bell', title: 'Alertas anticipadas', text: 'Detecta riesgos y oportunidades antes de que sucedan.' },
    { icon: 'calendar', title: 'Proyección 30/60/90', text: 'Planifica con escenarios basados en tus datos.' },
    { icon: 'shield', title: 'Seguro y confiable', text: 'Tus datos siempre protegidos con la mejor tecnología.' }
  ];

  protected readonly testimonials = [
    { quote: 'Desde que usamos CashPyme tenemos claridad en un viaje y eso ayuda planificar con más tranquilidad.', name: 'Camila Torres', role: 'Tienda de ropa', initials: 'CT' },
    { quote: 'Las proyecciones de 90 y 60 días me han ayudado a evitar situaciones difíciles y anticiparme.', name: 'Metas Reyes', role: 'Servicios de marketing', initials: 'MR' },
    { quote: 'El equipo dejó de vivir al límite y ahora tenemos una visión clara de lo que viene.', name: 'Daniela Fuentes', role: 'Cafetería', initials: 'DF' }
  ];

  protected currentProjection() {
    return this.projections[this.projectionDays() as keyof typeof this.projections];
  }

  protected toggleMenu() {
    this.isMenuOpen.update((isOpen) => !isOpen);
  }

  protected closeMenu() {
    this.isMenuOpen.set(false);
  }
}
