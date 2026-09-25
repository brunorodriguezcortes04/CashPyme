import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { CatalogosService, OpcionCatalogo, TipoMovimiento } from '../../core/catalogos.service';
import { Cuenta, CuentasService } from '../../core/cuentas.service';
import {
  Categoria,
  Movimiento,
  MovimientosService,
  RequiereConfirmacionError
} from '../../core/movimientos.service';

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
  /** Aviso de sobregiro: el movimiento se guardó, pero la cuenta quedó en negativo. */
  protected readonly warningMessage = signal<string | null>(null);
  protected readonly hasCuentas = computed(() => this.cuentas().length > 0);
  protected readonly hasCategorias = computed(() => this.categorias().length > 0);

  /** null = creando un movimiento nuevo; un id = editando ese movimiento. */
  protected readonly editandoId = signal<number | null>(null);
  protected readonly estaEditando = computed(() => this.editandoId() !== null);

  /** Movimiento sobre el que se pidió anular y el backend avisó que tiene pagos aplicados. */
  protected readonly confirmandoAnulacion = signal<{ id: number; mensaje: string } | null>(null);
  protected readonly anulandoId = signal<number | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    fecha: [hoy(), [Validators.required]],
    monto: [null as number | null, [Validators.required, Validators.min(0.01)]],
    idCuenta: [null as number | null, [Validators.required]],
    idCategoria: [null as number | null, [Validators.required]],
    medioPago: ['', [Validators.required]],
    descripcion: ['', [Validators.maxLength(250)]]
  });

  /** Filtros del listado. Viven aparte del formulario de registro: no se pisan entre sí. */
  protected readonly filtros = this.formBuilder.nonNullable.group({
    fechaDesde: [''],
    fechaHasta: [''],
    idCategoria: [null as number | null],
    idCuenta: [null as number | null]
  });

  /**
   * Si el listado vuelve vacío, el mensaje cambia según esto: sin filtros es "todavía no
   * registras nada"; con filtros es "no hay resultados para esta búsqueda", que es un
   * estado vacío, no un error.
   */
  protected readonly hayFiltrosAplicados = signal(false);
  protected readonly rangoInvalido = signal(false);

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
        this.cancelarEdicion();
        // Al cambiar de ingresos a egresos las categorías ya no aplican: se parte limpio.
        this.limpiarFiltros({ recargar: false });
        this.cargarDatos();
      });
  }

  /**
   * El MVP no bloquea el sobregiro: el movimiento ya se guardó. Esto solo avisa, después
   * de recalcular el saldo, que la cuenta quedó en negativo, para que la persona se entere
   * en el momento y no al revisar la caja más tarde.
   */
  private avisarSiQuedaSobregirada(idCuenta: number): void {
    const cuenta = this.cuentas().find((c) => c.id === idCuenta);

    this.warningMessage.set(
      cuenta && cuenta.saldoActual < 0
        ? `La cuenta "${cuenta.nombreCuenta}" quedó con saldo negativo. El movimiento se guardó igual.`
        : null
    );
  }

  protected aplicarFiltros(): void {
    const { fechaDesde, fechaHasta } = this.filtros.getRawValue();

    if (fechaDesde && fechaHasta && fechaDesde > fechaHasta) {
      this.rangoInvalido.set(true);
      return;
    }

    this.rangoInvalido.set(false);
    this.cargarMovimientos();
  }

  protected limpiarFiltros(opciones: { recargar: boolean } = { recargar: true }): void {
    this.filtros.reset({ fechaDesde: '', fechaHasta: '', idCategoria: null, idCuenta: null });
    this.rangoInvalido.set(false);
    this.hayFiltrosAplicados.set(false);

    if (opciones.recargar) {
      this.cargarMovimientos();
    }
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { fecha, monto, idCuenta, idCategoria, medioPago, descripcion } = this.form.getRawValue();
    const payload = {
      fecha,
      monto: monto!,
      idCuenta: idCuenta!,
      idCategoria: idCategoria!,
      medioPago,
      descripcion: descripcion || null
    };

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.warningMessage.set(null);

    const idEditando = this.editandoId();
    const peticion = idEditando === null
      ? this.movimientosService.crearMovimiento({ ...payload, tipoMovimiento: this.tipo() })
      : this.movimientosService.actualizarMovimiento(idEditando, payload);

    peticion.subscribe({
      next: (movimiento) => {
        this.successMessage.set(
          idEditando === null ? this.tipoActual()?.mensajeExito ?? null : 'Movimiento actualizado.'
        );

        // Con filtros puestos el movimiento recién guardado puede no pertenecer al resultado
        // (otra fecha, otra categoría): se recarga en vez de insertarlo a mano, para que el
        // listado siempre coincida con lo que el filtro dice que está mostrando.
        if (this.hayFiltrosAplicados()) {
          this.cargarMovimientos();
        } else if (idEditando === null) {
          this.movimientos.update((actuales) => [movimiento, ...actuales]);
        } else {
          this.movimientos.update((actuales) =>
            actuales.map((m) => (m.id === movimiento.id ? movimiento : m))
          );
        }
        this.cuentasService.listCuentas().subscribe((cuentas) => {
          this.cuentas.set(cuentas);
          this.avisarSiQuedaSobregirada(movimiento.idCuenta);
        });
        this.editandoId.set(null);
        this.reiniciarFormulario(idEditando === null ? movimiento.idCuenta : null);
        this.isSubmitting.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }

  /** Solo movimientos registrados (sin pagos aplicados) se pueden editar; el backend igual lo valida. */
  protected editar(movimiento: Movimiento): void {
    this.editandoId.set(movimiento.id);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.warningMessage.set(null);
    this.form.setValue({
      fecha: movimiento.fecha,
      monto: movimiento.monto,
      idCuenta: movimiento.idCuenta,
      idCategoria: movimiento.idCategoria,
      medioPago: movimiento.medioPago,
      descripcion: movimiento.descripcion ?? ''
    });
  }

  protected cancelarEdicion(): void {
    this.editandoId.set(null);
    this.reiniciarFormulario();
  }

  /**
   * Primera llamada sin confirmar: si el backend avisa que hay pagos aplicados, se guarda el
   * aviso en confirmandoAnulacion() en vez de tratarlo como un error, para ofrecer el botón
   * de confirmar en vez de solo mostrar un mensaje rojo.
   */
  protected anular(movimiento: Movimiento): void {
    this.anulandoId.set(movimiento.id);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.warningMessage.set(null);
    this.confirmandoAnulacion.set(null);

    this.movimientosService.anularMovimiento(movimiento.id, null, false).subscribe({
      next: (actualizado) => this.onAnulado(actualizado),
      error: (error: unknown) => {
        this.anulandoId.set(null);
        if (error instanceof RequiereConfirmacionError) {
          this.confirmandoAnulacion.set({ id: movimiento.id, mensaje: error.message });
        } else {
          this.errorMessage.set(mensajeDeError(error));
        }
      }
    });
  }

  protected confirmarAnulacion(): void {
    const pendiente = this.confirmandoAnulacion();
    if (!pendiente) {
      return;
    }

    this.anulandoId.set(pendiente.id);

    this.movimientosService.anularMovimiento(pendiente.id, null, true).subscribe({
      next: (actualizado) => this.onAnulado(actualizado),
      error: (error: unknown) => {
        this.anulandoId.set(null);
        this.confirmandoAnulacion.set(null);
        this.errorMessage.set(mensajeDeError(error));
      }
    });
  }

  protected cancelarConfirmacion(): void {
    this.confirmandoAnulacion.set(null);
  }

  private onAnulado(movimiento: Movimiento): void {
    this.anulandoId.set(null);
    this.confirmandoAnulacion.set(null);
    this.successMessage.set('Movimiento anulado.');
    this.movimientos.update((actuales) =>
      actuales.map((m) => (m.id === movimiento.id ? movimiento : m))
    );
    if (this.editandoId() === movimiento.id) {
      this.cancelarEdicion();
    }
    this.cuentasService.listCuentas().subscribe((cuentas) => this.cuentas.set(cuentas));
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

    this.cargarMovimientos();
  }

  /**
   * Trae el listado con los filtros que estén puestos. Un resultado vacío es un estado
   * válido, no un error: solo cambia el mensaje que se muestra.
   */
  private cargarMovimientos(): void {
    const { fechaDesde, fechaHasta, idCategoria, idCuenta } = this.filtros.getRawValue();
    this.hayFiltrosAplicados.set(Boolean(fechaDesde || fechaHasta || idCategoria || idCuenta));

    this.isLoading.set(true);

    this.movimientosService
      .listMovimientos(this.tipo(), { fechaDesde, fechaHasta, idCategoria, idCuenta })
      .subscribe({
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