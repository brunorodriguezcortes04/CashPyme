import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService, EmpresaMembresia } from '../core/auth.service';
import { Pantalla, PantallasService } from '../core/pantallas.service';

@Component({
  selector: 'app-backoffice-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './backoffice-shell.html',
  styleUrl: './backoffice-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BackofficeShell {
  private readonly authService = inject(AuthService);
  private readonly pantallasService = inject(PantallasService);
  private readonly router = inject(Router);

  protected readonly session = this.authService.session;
  protected readonly empresas = signal<EmpresaMembresia[]>([]);
  protected readonly tieneVariasEmpresas = computed(() => this.empresas().length > 1);

  // El menú es lo que la tabla `pantalla` publique para este rol: el front no decide nada.
  protected readonly pantallas = signal<Pantalla[]>([]);

  constructor() {
    this.pantallasService.listPantallas().subscribe((pantallas) => this.pantallas.set(pantallas));

    this.authService.listEmpresas().subscribe({
      next: (empresas) => this.empresas.set(empresas),
      error: () => this.empresas.set([])
    });
  }

  protected cambiarEmpresa(event: Event): void {
    const idEmpresa = Number((event.target as HTMLSelectElement).value);

    this.authService.cambiarEmpresa(idEmpresa).subscribe({
      // Cambiar de empresa cambia rol, permisos, pantallas y datos cargados: se recarga limpio.
      next: () => location.reload()
    });
  }

  protected logout(): void {
    this.authService.logout();
    this.pantallasService.limpiarCache();
    this.router.navigateByUrl('/');
  }
}
