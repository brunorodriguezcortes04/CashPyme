import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { mensajeDeError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';

/** Mismo criterio que el registro: la confirmación tiene que calzar con la nueva. */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const passwordNueva = control.get('passwordNueva')?.value;
  const confirmPassword = control.get('confirmPassword')?.value;
  return passwordNueva === confirmPassword ? null : { passwordMismatch: true };
}

@Component({
  selector: 'app-cuenta',
  imports: [ReactiveFormsModule],
  templateUrl: './cuenta.html',
  styleUrl: './cuenta.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Cuenta {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);

  protected readonly session = this.authService.session;
  protected readonly showPassword = signal(false);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly reenviandoVerificacion = signal(false);
  protected readonly verificacionMensaje = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group(
    {
      passwordActual: ['', [Validators.required]],
      passwordNueva: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
      confirmPassword: ['', [Validators.required]]
    },
    { validators: passwordsMatchValidator }
  );

  protected togglePasswordVisibility(): void {
    this.showPassword.update((visible) => !visible);
  }

  /**
   * Si el correo ya estaba confirmado el backend responde 409 con su propio mensaje
   * ("Tu correo ya está verificado."), que se muestra tal cual.
   */
  protected reenviarVerificacion(): void {
    this.reenviandoVerificacion.set(true);
    this.verificacionMensaje.set(null);

    this.authService.reenviarVerificacion().subscribe({
      next: () => {
        this.verificacionMensaje.set(`Te enviamos un enlace nuevo a ${this.session()?.email ?? 'tu correo'}.`);
        this.reenviandoVerificacion.set(false);
      },
      error: (error: unknown) => {
        this.verificacionMensaje.set(mensajeDeError(error));
        this.reenviandoVerificacion.set(false);
      }
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { passwordActual, passwordNueva } = this.form.getRawValue();

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.authService.cambiarPassword(passwordActual, passwordNueva).subscribe({
      next: () => {
        // La sesión sigue abierta: el JWT no depende de la contraseña.
        this.successMessage.set('Tu contraseña se cambió correctamente.');
        this.form.reset({ passwordActual: '', passwordNueva: '', confirmPassword: '' });
        this.isSubmitting.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }
}
