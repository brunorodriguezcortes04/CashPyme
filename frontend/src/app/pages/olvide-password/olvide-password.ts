import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-olvide-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './olvide-password.html',
  styleUrl: './olvide-password.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class OlvidePassword {
  private readonly authService = inject(AuthService);

  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  /** Correo al que se pidió el enlace; mientras sea null se muestra el formulario. */
  protected readonly enviadoA = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

  protected submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    const { email } = this.form.getRawValue();

    this.authService.olvidePassword(email).subscribe({
      next: () => {
        this.enviadoA.set(email);
        this.isSubmitting.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }
}
