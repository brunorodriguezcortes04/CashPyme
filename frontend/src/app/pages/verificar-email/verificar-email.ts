import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';

type Estado = 'verificando' | 'ok' | 'error';

/**
 * Destino del enlace del correo "Confirma tu correo": /verificar-email?token=…
 * Verifica apenas se abre: la persona ya hizo clic en el correo, pedirle otro clic acá
 * sería un paso de más.
 */
@Component({
  selector: 'app-verificar-email',
  imports: [RouterLink],
  templateUrl: './verificar-email.html',
  styleUrl: './verificar-email.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class VerificarEmail {
  private readonly authService = inject(AuthService);

  protected readonly estado = signal<Estado>('verificando');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly isAuthenticated = this.authService.isAuthenticated;

  constructor() {
    const token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
    if (!token) {
      this.estado.set('error');
      this.errorMessage.set('Este enlace está incompleto. Ábrelo directamente desde el correo que te enviamos.');
      return;
    }

    this.authService.verificarEmail(token).subscribe({
      next: () => this.estado.set('ok'),
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.estado.set('error');
      }
    });
  }
}
