import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { mensajeDeError } from '../../core/api-error';
import { Empresa, EmpresaService } from '../../core/empresa.service';

@Component({
  selector: 'app-empresa',
  imports: [ReactiveFormsModule],
  templateUrl: './empresa.html',
  styleUrl: './empresa.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EmpresaPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly empresaService = inject(EmpresaService);

  protected readonly empresa = signal<Empresa | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    razonSocial: ['', [Validators.required, Validators.maxLength(150)]],
    rut: ['', [Validators.maxLength(12)]],
    giro: ['', [Validators.maxLength(150)]],
    direccion: ['', [Validators.maxLength(200)]],
    telefono: ['', [Validators.maxLength(20)]],
    emailContacto: ['', [Validators.email, Validators.maxLength(150)]],
    // Vacíos hasta que llega la empresa: los valores por defecto los pone el backend al crearla.
    diasAvisoVencimiento: [null as number | null, [Validators.required, Validators.min(0), Validators.max(60)]],
    umbralSaldoBajo: [null as number | null, [Validators.required, Validators.min(0)]]
  });

  constructor() {
    this.cargar();
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.empresaService
      .actualizar({
        razonSocial: raw.razonSocial,
        rut: raw.rut || null,
        giro: raw.giro || null,
        direccion: raw.direccion || null,
        telefono: raw.telefono || null,
        emailContacto: raw.emailContacto || null,
        diasAvisoVencimiento: raw.diasAvisoVencimiento!,
        umbralSaldoBajo: raw.umbralSaldoBajo!
      })
      .subscribe({
        next: (empresa) => {
          this.empresa.set(empresa);
          // El backend guarda el RUT normalizado (sin puntos, con guion): se muestra como quedó.
          this.form.controls.rut.setValue(empresa.rut ?? '');
          this.successMessage.set('Datos de la empresa actualizados.');
          this.isSubmitting.set(false);
        },
        error: (error: unknown) => {
          this.errorMessage.set(mensajeDeError(error));
          this.isSubmitting.set(false);
        }
      });
  }

  private cargar(): void {
    this.isLoading.set(true);

    this.empresaService.obtener().subscribe({
      next: (empresa) => {
        this.empresa.set(empresa);
        this.form.setValue({
          razonSocial: empresa.razonSocial,
          rut: empresa.rut ?? '',
          giro: empresa.giro ?? '',
          direccion: empresa.direccion ?? '',
          telefono: empresa.telefono ?? '',
          emailContacto: empresa.emailContacto ?? '',
          diasAvisoVencimiento: empresa.diasAvisoVencimiento,
          umbralSaldoBajo: empresa.umbralSaldoBajo
        });
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }
}