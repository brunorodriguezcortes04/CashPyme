import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';

function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return password === confirmPassword ? null : { passwordMismatch: true };
}

/**
 * Destino del enlace del correo "Restablece tu contraseña": /restablecer-password?token=…
 * El token no se valida al abrir la página sino al enviar el formulario, para no gastar
 * una llamada extra; si venció, el error del backend lo dice y se ofrece pedir otro.
 */
@Component({
  selector: 'app-restablecer-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './restablecer-password.html',
  styleUrl: './restablecer-password.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RestablecerPassword {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
  protected readonly showPassword = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
      confirmPassword: ['', [Validators.required]]
    },
    { validators: passwordsMatchValidator }
  );

  protected togglePasswordVisibility() {
    this.showPassword.update((visible) => !visible);
  }

  protected submit() {
    if (!this.token) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    this.authService.restablecerPassword(this.token, this.form.getRawValue().password).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.router.navigate(['/login'], { queryParams: { passwordRestablecida: 1 } });
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }
}
