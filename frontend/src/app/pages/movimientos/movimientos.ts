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

  /** Error al traer el listado (por ejemplo un rango de fechas al revés): se muestra junto a los filtros. */
  protected readonly errorListado = signal<string | null>(null);

  /** Si el rango que aplicó el backend es el mes en curso de la empresa (el que usa sin filtros). */
  private readonly periodoPorDefecto = signal(true);

  private readonly tamanoPagina = 20;
  protected readonly pagina = signal(1);
  protected readonly totalResultados = signal(0);
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.totalResultados() / this.tamanoPagina))
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    fecha: [hoy(), [Validators.required]],
    monto: [null as number | null, [Validators.required, Validators.min(0.01)]],
    idCuenta: [null as number | null, [Validators.required]],
    idCategoria: [null as number | null, [Validators.required]],
    medioPago: ['', [Validators.required]],
    descripcion: ['', [Validators.maxLength(250)]]
  });

  /**
   * Las fechas parten vacías: sin rango, el backend aplica el mes en curso de la empresa y
   * devuelve cuál fue, y con eso se llenan los campos.
   */
  protected readonly filtrosForm = this.formBuilder.nonNullable.group({
    fechaDesde: [''],
    fechaHasta: [''],
    idCategoria: [''],
    idCuenta: ['']
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
        this.cancelarEdicion();
        this.pagina.set(1);
        // Al cambiar de ingresos a egresos las categorías ya no aplican: se parte limpio.
        this.filtrosForm.reset({ fechaDesde: '', fechaHasta: '', idCategoria: '', idCuenta: '' });
        this.cargarDatos();
      });
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
          idEditando === null
            ? (this.tipoActual()?.mensajeExito ?? 'Movimiento registrado.')
            : 'Movimiento actualizado.'
        );
        // El aviso de sobregiro (si lo hay) lo arma el backend; se recargan las cuentas para
        // que el panel de saldos muestre el saldo nuevo.
        this.warningMessage.set(movimiento.advertencia ?? null);
        this.cuentasService.listCuentas().subscribe((cuentas) => this.cuentas.set(cuentas));
        this.editandoId.set(null);
        this.reiniciarFormulario(idEditando === null ? movimiento.idCuenta : null);
        this.isSubmitting.set(false);

        // El listado va por fecha descendente: un movimiento recién creado vive en la
        // página 1, así que quedarse en la que estabas lo escondería. Al editar se respeta
        // la página actual, que es donde está la fila que acabas de tocar.
        if (idEditando === null) {
          this.pagina.set(1);
        }
        this.cargarMovimientos();
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }

  /** Solo se ofrece cuando el backend lo permite (movimiento.acciones.puedeEditar). */
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
      next: () => this.onAnulado(movimiento.id),
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
      next: () => this.onAnulado(pendiente.id),
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

  /** Un rango al revés lo rechaza el backend con su mensaje, que se muestra junto a los filtros. */
  protected aplicarFiltros(): void {
    // Filtro nuevo, resultados nuevos: quedarse en la página 3 del filtro anterior no
    // significa nada para este.
    this.pagina.set(1);
    this.cargarMovimientos();
  }

  /** Sin fechas, el backend vuelve al mes en curso. */
  protected limpiarFiltros(): void {
    this.filtrosForm.reset({ fechaDesde: '', fechaHasta: '', idCategoria: '', idCuenta: '' });
    this.pagina.set(1);
    this.cargarMovimientos();
  }

  /** Método normal, no computed(): el formulario de filtros no es una señal. */
  protected hayFiltrosActivos(): boolean {
    const f = this.filtrosForm.getRawValue();
    return Boolean(f.idCategoria || f.idCuenta) || !this.periodoPorDefecto();
  }

  protected paginaAnterior(): void {
    if (this.pagina() > 1) {
      this.pagina.update((p) => p - 1);
      this.cargarMovimientos();
    }
  }

  protected paginaSiguiente(): void {
    if (this.pagina() < this.totalPaginas()) {
      this.pagina.update((p) => p + 1);
      this.cargarMovimientos();
    }
  }

  private onAnulado(idAnulado: number): void {
    this.anulandoId.set(null);
    this.confirmandoAnulacion.set(null);
    this.successMessage.set('Movimiento anulado.');

    // Si se estaba editando justo el movimiento que se anuló, el formulario apuntaría a una
    // fila que ya no se puede guardar (el backend responde MovimientoYaAnuladoException):
    // se sale de edición en vez de dejar que la persona escriba para nada.
    if (this.editandoId() === idAnulado) {
      this.cancelarEdicion();
    }

    this.cuentasService.listCuentas().subscribe((cuentas) => this.cuentas.set(cuentas));
    this.cargarMovimientos();
  }

  private cargarDatos(): void {
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
   * Trae una página del listado con los filtros que estén puestos. Un resultado vacío es un
   * estado válido, no un error: solo cambia el mensaje que se muestra.
   */
  private cargarMovimientos(): void {
    this.isLoading.set(true);
    this.errorListado.set(null);
    const f = this.filtrosForm.getRawValue();

    this.movimientosService
      .listMovimientos({
        tipo: this.tipo(),
        fechaDesde: f.fechaDesde || undefined,
        fechaHasta: f.fechaHasta || undefined,
        idCategoria: f.idCategoria ? Number(f.idCategoria) : undefined,
        idCuenta: f.idCuenta ? Number(f.idCuenta) : undefined,
        pagina: this.pagina(),
        tamanoPagina: this.tamanoPagina
      })
      .subscribe({
        next: (resultado) => {
          this.totalResultados.set(resultado.totalResultados);

          // La página pedida puede haber quedado fuera de rango: pasa al anular el último
          // movimiento de la última página. Los controles de paginación solo se dibujan
          // cuando hay filas, así que sin esto se queda atrapado en una página vacía sin
          // botón para volver. Retrocede una sola vez: ultimaPagina ya es válida.
          const ultimaPagina = Math.max(1, Math.ceil(resultado.totalResultados / this.tamanoPagina));
          if (resultado.items.length === 0 && this.pagina() > ultimaPagina) {
            this.pagina.set(ultimaPagina);
            this.cargarMovimientos();
            return;
          }

          // Los campos muestran el rango que de verdad se aplicó (el mes en curso si no se pidió otro).
          this.filtrosForm.patchValue(
            { fechaDesde: resultado.fechaDesde ?? '', fechaHasta: resultado.fechaHasta ?? '' },
            { emitEvent: false }
          );
          this.periodoPorDefecto.set(resultado.periodoPorDefecto);
          this.movimientos.set(resultado.items);
          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.errorListado.set(mensajeDeError(error));
          this.movimientos.set([]);
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