import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Login {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly showPassword = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  /**
   * Llegó acá porque se le venció la sesión, no porque quisiera entrar. Sin este aviso el
   * login aparece en blanco y parece que la aplicación lo expulsó sin motivo.
   */
  protected readonly sesionExpirada = signal(
    inject(ActivatedRoute).snapshot.queryParamMap.has('sesionExpirada')
  );

  /** Viene de restablecer la contraseña con el enlace del correo. */
  protected readonly passwordRestablecida = signal(
    inject(ActivatedRoute).snapshot.queryParamMap.has('passwordRestablecida')
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    rememberMe: [false]
  });

  protected togglePasswordVisibility() {
    this.showPassword.update((visible) => !visible);
  }

  protected submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    this.authService.login(this.form.getRawValue()).subscribe({
      next: () => {
        // Si un guard rebota la navegación el componente sobrevive, y sin esto el botón se
        // queda en "Ingresando…" para siempre aunque el login haya funcionado.
        this.isSubmitting.set(false);
        this.router.navigateByUrl('/backoffice');
      },
      error: (error: unknown) => {
        this.errorMessage.set(error instanceof Error ? error.message : 'Ocurrió un error inesperado. Inténtalo nuevamente.');
        this.isSubmitting.set(false);
      }
    });
  }
}
