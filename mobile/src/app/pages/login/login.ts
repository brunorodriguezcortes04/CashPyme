import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { IonButton, IonContent, IonInput, IonSpinner, IonText } from '@ionic/angular';
import { mensajeDeError } from '../../core/api.service';
import { SesionService } from '../../core/sesion.service';

/** Login con el mismo endpoint y las mismas credenciales que la web. */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, IonContent, IonInput, IonButton, IonSpinner, IonText],
  templateUrl: './login.html',
  styleUrl: './login.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Login {
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);

  protected readonly enviando = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]]
  });

  protected async entrar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();

    try {
      await this.sesion.iniciar(email.trim(), password);
      await this.router.navigateByUrl('/resumen', { replaceUrl: true });
    } catch (e) {
      this.error.set(mensajeDeError(e));
    } finally {
      this.enviando.set(false);
    }
  }
}
