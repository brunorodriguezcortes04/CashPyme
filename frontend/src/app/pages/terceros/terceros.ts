import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { mensajeDeError } from '../../core/api-error';
import { CatalogosService, OpcionCatalogo } from '../../core/catalogos.service';
import { normalizarRut, validadorRut } from '../../core/rut.validator';
import { Tercero, TercerosService } from '../../core/terceros.service';

@Component({
  selector: 'app-terceros',
  imports: [ReactiveFormsModule],
  templateUrl: './terceros.html',
  styleUrl: './terceros.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Terceros {
  private readonly formBuilder = inject(FormBuilder);
  private readonly tercerosService = inject(TercerosService);
  private readonly catalogosService = inject(CatalogosService);

  protected readonly tiposTercero = signal<OpcionCatalogo[]>([]);
  protected readonly terceros = signal<Tercero[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  /** null = creando; un id = editando ese tercero. */
  protected readonly editandoId = signal<number | null>(null);
  protected readonly estaEditando = computed(() => this.editandoId() !== null);

  protected readonly form = this.formBuilder.nonNullable.group({
    tipoTercero: ['', [Validators.required]],
    nombreRazonSocial: ['', [Validators.required, Validators.maxLength(150)]],
    rut: ['', [validadorRut()]],
    giro: ['', [Validators.maxLength(150)]],
    email: ['', [Validators.email, Validators.maxLength(150)]],
    telefono: ['', [Validators.maxLength(20)]],
    direccion: ['', [Validators.maxLength(200)]]
  });

  constructor() {
    this.catalogosService.listTiposTercero().subscribe({
      next: (tipos) => {
        this.tiposTercero.set(tipos);
        this.form.controls.tipoTercero.setValue(this.tipoPorDefecto());
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.cargarTerceros();
  }

  protected etiquetaTipo(valor: string): string {
    return this.tiposTercero().find((tipo) => tipo.valor === valor)?.etiqueta ?? valor;
  }

  /** Deja el RUT formateado apenas la persona sale del campo, antes de guardar. */
  protected formatearRut(): void {
    const valor = this.form.controls.rut.value.trim();
    if (valor) {
      this.form.controls.rut.setValue(normalizarRut(valor));
    }
  }

  protected editar(tercero: Tercero): void {
    this.editandoId.set(tercero.id);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.form.setValue({
      tipoTercero: tercero.tipoTercero,
      nombreRazonSocial: tercero.nombreRazonSocial,
      rut: tercero.rut ?? '',
      giro: tercero.giro ?? '',
      email: tercero.email ?? '',
      telefono: tercero.telefono ?? '',
      direccion: tercero.direccion ?? ''
    });
  }

  protected cancelarEdicion(): void {
    this.editandoId.set(null);
    this.resetForm();
  }

  protected cambiarEstado(tercero: Tercero): void {
    this.errorMessage.set(null);

    this.tercerosService.cambiarEstado(tercero.id, !tercero.activo).subscribe({
      next: () => {
        this.successMessage.set(tercero.activo ? 'Desactivado.' : 'Activado.');
        this.cargarTerceros();
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.formatearRut();
    const { tipoTercero, nombreRazonSocial, rut, giro, email, telefono, direccion } = this.form.getRawValue();
    const payload = {
      tipoTercero,
      nombreRazonSocial,
      rut: rut || null,
      giro: giro || null,
      email: email || null,
      telefono: telefono || null,
      direccion: direccion || null
    };

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const id = this.editandoId();
    const peticion = id === null
      ? this.tercerosService.crear(payload)
      : this.tercerosService.actualizar(id, payload);

    peticion.subscribe({
      next: () => {
        this.successMessage.set(id === null ? 'Guardado correctamente.' : 'Cambios guardados.');
        this.editandoId.set(null);
        this.resetForm();
        this.isSubmitting.set(false);
        this.cargarTerceros();
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }

  private cargarTerceros(): void {
    this.isLoading.set(true);

    this.tercerosService.listTerceros(true).subscribe({
      next: (terceros) => {
        this.terceros.set(terceros);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }

  private resetForm(): void {
    this.form.reset({
      tipoTercero: this.tipoPorDefecto(),
      nombreRazonSocial: '',
      rut: '',
      giro: '',
      email: '',
      telefono: '',
      direccion: ''
    });
  }

  private tipoPorDefecto(): string {
    return this.tiposTercero()[0]?.valor ?? '';
  }
}