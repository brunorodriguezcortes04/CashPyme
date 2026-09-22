import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { CatalogosService, OpcionCatalogo, TipoMovimiento } from '../../core/catalogos.service';
import { Cuenta, CuentasService } from '../../core/cuentas.service';
import { Categoria, Movimiento, MovimientosService } from '../../core/movimientos.service';

function hoy(): string {
  const ahora = new Date();
  const mes = String(ahora.getMonth() + 1).padStart(2, '0');
  const dia = String(ahora.getDate()).padStart(2, '0');
  return `${ahora.getFullYear()}-${mes}-${dia}`;
}

@Component({
  selector: 'app-movimientos',
  imports: [ReactiveFormsModule, DecimalPipe, RouterLink],
  templateUrl: './movimientos.html',
  styleUrl: './movimientos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Movimientos {
  private readonly formBuilder = inject(FormBuilder);
  private readonly movimientosService = inject(MovimientosService);
  private readonly cuentasService = inject(CuentasService);
  private readonly catalogosService = inject(CatalogosService);

  private readonly tipo = signal('');
  private readonly tiposMovimiento = signal<TipoMovimiento[]>([]);

  /** Todo el vocabulario visible del tipo viene del backend; acá solo se arma la frase. */
  protected readonly tipoActual = computed(() =>
    this.tiposMovimiento().find((t) => t.valor === this.tipo()) ?? null
  );

  protected readonly mediosPago = signal<OpcionCatalogo[]>([]);
  protected readonly cuentas = signal<Cuenta[]>([]);
  protected readonly categorias = signal<Categoria[]>([]);
  protected readonly movimientos = signal<Movimiento[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly hasCuentas = computed(() => this.cuentas().length > 0);
  protected readonly hasCategorias = computed(() => this.categorias().length > 0);

  protected readonly form = this.formBuilder.nonNullable.group({
    fecha: [hoy(), [Validators.required]],
    monto: [null as number | null, [Validators.required, Validators.min(0.01)]],
    idCuenta: [null as number | null, [Validators.required]],
    idCategoria: [null as number | null, [Validators.required]],
    medioPago: ['', [Validators.required]],
    descripcion: ['', [Validators.maxLength(250)]]
  });

  constructor() {
    this.catalogosService.listTiposMovimiento().subscribe({
      next: (tipos) => this.tiposMovimiento.set(tipos),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.catalogosService.listMediosPago().subscribe({
      next: (medios) => {
        this.mediosPago.set(medios);
        this.form.controls.medioPago.setValue(this.medioPagoPorDefecto());
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    // Ingresos y egresos comparten ruta: al cambiar de tipo se recargan los datos.
    inject(ActivatedRoute)
      .paramMap.pipe(takeUntilDestroyed())
      .subscribe((params) => {
        this.tipo.set(params.get('tipo') ?? '');
        this.reiniciarFormulario();
        this.cargarDatos();
      });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { fecha, monto, idCuenta, idCategoria, medioPago, descripcion } = this.form.getRawValue();

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.movimientosService
      .crearMovimiento({
        tipoMovimiento: this.tipo(),
        fecha,
        monto: monto!,
        idCuenta: idCuenta!,
        idCategoria: idCategoria!,
        medioPago,
        descripcion: descripcion || null
      })
      .subscribe({
        next: (movimiento) => {
          this.movimientos.update((actuales) => [movimiento, ...actuales]);
          this.cuentasService.listCuentas().subscribe((cuentas) => this.cuentas.set(cuentas));
          this.successMessage.set(this.tipoActual()?.mensajeExito ?? null);
          this.reiniciarFormulario(movimiento.idCuenta);
          this.isSubmitting.set(false);
        },
        error: (error: unknown) => {
          this.errorMessage.set(mensajeDeError(error));
          this.isSubmitting.set(false);
        }
      });
  }

  private cargarDatos(): void {
    this.isLoading.set(true);

    this.cuentasService.listCuentas().subscribe({
      next: (cuentas) => this.cuentas.set(cuentas),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.movimientosService.listCategorias(this.tipo()).subscribe({
      next: (categorias) => this.categorias.set(categorias),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.movimientosService.listMovimientos(this.tipo()).subscribe({
      next: (movimientos) => {
        this.movimientos.set(movimientos);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }

  private reiniciarFormulario(idCuenta: number | null = null): void {
    this.form.reset({
      fecha: hoy(),
      monto: null,
      idCuenta,
      idCategoria: null,
      medioPago: this.medioPagoPorDefecto(),
      descripcion: ''
    });
  }

  private medioPagoPorDefecto(): string {
    return this.mediosPago()[0]?.valor ?? '';
  }
}
