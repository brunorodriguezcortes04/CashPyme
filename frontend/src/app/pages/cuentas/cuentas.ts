import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { mensajeDeError } from '../../core/api-error';
import { CatalogosService, OpcionCatalogo } from '../../core/catalogos.service';
import { Cuenta, CuentasService } from '../../core/cuentas.service';

@Component({
  selector: 'app-cuentas',
  imports: [ReactiveFormsModule, DecimalPipe],
  templateUrl: './cuentas.html',
  styleUrl: './cuentas.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Cuentas {
  private readonly formBuilder = inject(FormBuilder);
  private readonly cuentasService = inject(CuentasService);
  private readonly catalogosService = inject(CatalogosService);

  protected readonly tiposCuenta = signal<OpcionCatalogo[]>([]);
  protected readonly cuentas = signal<Cuenta[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  /** null = creando; un id = editando esa cuenta. */
  protected readonly editandoId = signal<number | null>(null);
  protected readonly estaEditando = computed(() => this.editandoId() !== null);

  protected readonly form = this.formBuilder.nonNullable.group({
    nombreCuenta: ['', [Validators.required, Validators.maxLength(100)]],
    tipoCuenta: ['', [Validators.required]],
    banco: ['', [Validators.maxLength(100)]],
    numeroCuenta: ['', [Validators.maxLength(50)]],
    saldoInicial: [0, [Validators.required]]
  });

  constructor() {
    this.catalogosService.listTiposCuenta().subscribe({
      next: (tipos) => {
        this.tiposCuenta.set(tipos);
        this.form.controls.tipoCuenta.setValue(this.tipoPorDefecto());
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.cargarCuentas();
  }

  protected etiquetaTipo(valor: string): string {
    return this.tiposCuenta().find((tipo) => tipo.valor === valor)?.etiqueta ?? valor;
  }

  protected editar(cuenta: Cuenta): void {
    this.editandoId.set(cuenta.id);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.form.setValue({
      nombreCuenta: cuenta.nombreCuenta,
      tipoCuenta: cuenta.tipoCuenta,
      banco: cuenta.banco ?? '',
      numeroCuenta: cuenta.numeroCuenta ?? '',
      saldoInicial: cuenta.saldoInicial
    });
  }

  protected cancelarEdicion(): void {
    this.editandoId.set(null);
    this.resetForm();
  }

  protected cambiarEstado(cuenta: Cuenta): void {
    this.errorMessage.set(null);

    this.cuentasService.cambiarEstado(cuenta.id, !cuenta.activo).subscribe({
      next: () => {
        this.successMessage.set(cuenta.activo ? 'Cuenta desactivada.' : 'Cuenta activada.');
        this.cargarCuentas();
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { nombreCuenta, tipoCuenta, banco, numeroCuenta, saldoInicial } = this.form.getRawValue();
    const payload = {
      nombreCuenta,
      tipoCuenta,
      banco: banco || null,
      numeroCuenta: numeroCuenta || null,
      saldoInicial
    };

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const id = this.editandoId();
    const peticion = id === null
      ? this.cuentasService.crear(payload)
      : this.cuentasService.actualizar(id, payload);

    peticion.subscribe({
      next: () => {
        this.successMessage.set(id === null ? 'Cuenta creada correctamente.' : 'Cuenta actualizada.');
        this.editandoId.set(null);
        this.resetForm();
        this.isSubmitting.set(false);
        this.cargarCuentas();
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }

  private cargarCuentas(): void {
    this.isLoading.set(true);

    this.cuentasService.listCuentas(true).subscribe({
      next: (cuentas) => {
        this.cuentas.set(cuentas);
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
      nombreCuenta: '',
      tipoCuenta: this.tipoPorDefecto(),
      banco: '',
      numeroCuenta: '',
      saldoInicial: 0
    });
  }

  private tipoPorDefecto(): string {
    return this.tiposCuenta()[0]?.valor ?? '';
  }
}
