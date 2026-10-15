import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { CatalogosService, TipoCuentaDocumento, TipoDocumento } from '../../core/catalogos.service';
import {
  Documento,
  DocumentoCorregible,
  DocumentosService,
  MovimientoAplicable,
  Pago
} from '../../core/documentos.service';
import { Categoria, MovimientosService } from '../../core/movimientos.service';
import { Tercero } from '../../core/terceros.service';

function aIso(fecha: Date): string {
  const mes = String(fecha.getMonth() + 1).padStart(2, '0');
  const dia = String(fecha.getDate()).padStart(2, '0');
  return `${fecha.getFullYear()}-${mes}-${dia}`;
}

function hoy(): string {
  return aIso(new Date());
}

function enDias(dias: number): string {
  const fecha = new Date();
  fecha.setDate(fecha.getDate() + dias);
  return aIso(fecha);
}

/**
 * Cuentas por cobrar y cuentas por pagar: una sola pantalla para las dos. El tipo de cuenta
 * viaja en la ruta (data.tipoCuenta) y todo lo que cambia entre una y otra lo entrega el
 * backend: los textos (/api/opciones/tipos-cuenta-documento), con quién se puede registrar
 * (/api/documentos/terceros), qué documentos admiten una nota (/api/documentos/corregibles) y
 * cómo se llama cada estado (etiquetaEstado), qué botones ofrece cada fila (acciones) y el
 * plazo de vencimiento que se propone. Las reglas que cruzan campos (neto + IVA = total,
 * vencimiento no anterior a la emisión) también las valida el backend, y su mensaje se muestra
 * tal cual. En el formulario solo quedan las restricciones de cada campo.
 */
@Component({
  selector: 'app-documentos',
  imports: [ReactiveFormsModule, DecimalPipe, RouterLink],
  templateUrl: './documentos.html',
  styleUrl: './documentos.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Documentos {
  private readonly formBuilder = inject(FormBuilder);
  private readonly documentosService = inject(DocumentosService);
  private readonly movimientosService = inject(MovimientosService);
  private readonly catalogosService = inject(CatalogosService);

  /** 'por_cobrar' o 'por_pagar'. Cada una tiene su ruta, así que no cambia mientras vive el componente. */
  private readonly tipoCuenta: string = inject(ActivatedRoute).snapshot.data['tipoCuenta'];

  /** Textos y datos de la pantalla según el tipo de cuenta. null hasta que responde el backend. */
  protected readonly pantalla = signal<TipoCuentaDocumento | null>(null);

  protected readonly tiposDocumento = signal<TipoDocumento[]>([]);
  protected readonly categorias = signal<Categoria[]>([]);
  protected readonly documentos = signal<Documento[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  /** Clientes o proveedores con los que se puede registrar este tipo de cuenta. */
  protected readonly terceros = signal<Tercero[]>([]);
  protected readonly hasTerceros = computed(() => this.terceros().length > 0);

  /** Documentos a los que se les puede emitir una nota. Se piden al entrar en modo nota. */
  protected readonly corregibles = signal<DocumentoCorregible[]>([]);

  /** null = creando un documento nuevo; un id = editando ese. */
  protected readonly editandoId = signal<number | null>(null);
  protected readonly estaEditando = computed(() => this.editandoId() !== null);

  protected readonly confirmandoAnulacion = signal<Documento | null>(null);
  protected readonly anulandoId = signal<number | null>(null);

  /** Documento al que se le está aplicando un cobro o un pago, con sus datos cargados. */
  protected readonly aplicando = signal<Documento | null>(null);
  protected readonly movimientosAplicables = signal<MovimientoAplicable[]>([]);
  protected readonly pagosDelDocumento = signal<Pago[]>([]);
  protected readonly cargandoAplicacion = signal(false);
  protected readonly errorAplicacion = signal<string | null>(null);

  /** Notas del documento que se está mirando, para explicar de dónde sale su saldo. */
  protected readonly notasVisibles = signal<{ documento: Documento; notas: Documento[] } | null>(null);

  private readonly tamanoPagina = 20;
  protected readonly pagina = signal(1);
  protected readonly totalResultados = signal(0);
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.totalResultados() / this.tamanoPagina))
  );

  protected readonly form = this.formBuilder.nonNullable.group(
    {
      idTercero: [null as number | null],
      tipoDocumento: ['', [Validators.required]],
      idDocumentoReferencia: [null as number | null],
      numeroDocumento: ['', [Validators.maxLength(50)]],
      montoTotal: [null as number | null, [Validators.required, Validators.min(0.01)]],
      montoNeto: [null as number | null, [Validators.min(0)]],
      montoIva: [0, [Validators.min(0)]],
      fechaEmision: [hoy(), [Validators.required]],
      // Se completa cuando llega la pantalla, con el plazo que define el backend.
      fechaVencimiento: ['', [Validators.required]],
      idCategoria: [null as number | null],
      descripcion: ['', [Validators.maxLength(250)]]
    },
    { validators: (control: AbstractControl) => this.camposSegunTipo(control) }
  );

  protected readonly formAplicar = this.formBuilder.nonNullable.group({
    idMovimiento: [null as number | null, [Validators.required]],
    montoAplicado: [null as number | null, [Validators.required, Validators.min(0.01)]]
  });

  constructor() {
    this.catalogosService.listTiposCuentaDocumento().subscribe({
      next: (tipos) => {
        const pantalla = tipos.find((t) => t.valor === this.tipoCuenta) ?? null;
        this.pantalla.set(pantalla);
        if (!pantalla) {
          this.errorMessage.set('Esta pantalla no está disponible.');
          return;
        }

        this.form.controls.fechaVencimiento.setValue(this.vencimientoPropuesto());

        // La categoría de un documento es del mismo tipo que el movimiento con que se paga.
        this.movimientosService.listCategorias(pantalla.flujo).subscribe({
          next: (categorias) => this.categorias.set(categorias),
          error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
        });
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.catalogosService.listTiposDocumento().subscribe({
      next: (tipos) => {
        this.tiposDocumento.set(tipos);
        this.form.controls.tipoDocumento.setValue(tipos.find((t) => !t.esNota)?.valor ?? '');
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.documentosService.listTerceros(this.tipoCuenta).subscribe({
      next: (terceros) => this.terceros.set(terceros),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    // Al pasar a nota (desde el botón de la fila o eligiendo el tipo a mano) se piden los
    // documentos que admiten una, con su saldo al día.
    this.form.controls.tipoDocumento.valueChanges.pipe(takeUntilDestroyed()).subscribe((tipo) => {
      if (this.esNota(tipo)) {
        this.cargarCorregibles();
      }
    });

    this.cargarDocumentos();
  }

  protected esNota(tipoDocumento: string): boolean {
    return this.tiposDocumento().find((t) => t.valor === tipoDocumento)?.esNota ?? false;
  }

  /** Método normal, no computed(): el formulario no es una señal. */
  protected notaEnCurso(): boolean {
    return this.esNota(this.form.controls.tipoDocumento.value);
  }

  protected etiquetaTipoDocumento(valor: string): string {
    return this.tiposDocumento().find((t) => t.valor === valor)?.etiqueta ?? valor;
  }

  protected tieneNotas(documento: Documento): boolean {
    return documento.montoNotasCredito > 0 || documento.montoNotasDebito > 0;
  }

  /**
   * Qué campo es obligatorio depende de qué campos muestra el formulario: una nota pide el
   * documento que corrige (tercero y vencimiento los toma de él), el resto pide el tercero.
   */
  private camposSegunTipo(control: AbstractControl): ValidationErrors | null {
    if (this.esNota(control.get('tipoDocumento')?.value)) {
      return control.get('idDocumentoReferencia')?.value ? null : { referenciaRequerida: true };
    }

    return control.get('idTercero')?.value ? null : { terceroRequerido: true };
  }

  /** Fecha de vencimiento que propone el formulario: hoy más el plazo que define el backend. */
  private vencimientoPropuesto(): string {
    return enDias(this.pantalla()?.diasVencimientoPorDefecto ?? 0);
  }

  protected submit(): void {
    const pantalla = this.pantalla();
    if (!pantalla || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const valores = this.form.getRawValue();
    const esNota = this.esNota(valores.tipoDocumento);

    const comunes = {
      tipoDocumento: valores.tipoDocumento,
      montoTotal: valores.montoTotal!,
      fechaEmision: valores.fechaEmision,
      numeroDocumento: valores.numeroDocumento || null,
      idCategoria: valores.idCategoria,
      montoNeto: valores.montoNeto,
      montoIva: valores.montoIva,
      descripcion: valores.descripcion || null
    };

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const idEditando = this.editandoId();
    const peticion =
      idEditando === null
        ? this.documentosService.crear({
            ...comunes,
            tipoCuenta: this.tipoCuenta,
            // Una nota va sin tercero ni vencimiento: los pone el backend a partir del
            // documento que corrige.
            idTercero: esNota ? null : valores.idTercero,
            fechaVencimiento: esNota ? null : valores.fechaVencimiento,
            idDocumentoReferencia: esNota ? valores.idDocumentoReferencia : null
          })
        : this.documentosService.actualizar(idEditando, {
            ...comunes,
            idTercero: valores.idTercero!,
            fechaVencimiento: valores.fechaVencimiento
          });

    peticion.subscribe({
      next: () => {
        this.successMessage.set(
          idEditando !== null
            ? 'Cambios guardados.'
            : esNota
              ? 'Nota emitida. El saldo del documento que corrige ya quedó ajustado.'
              : pantalla.mensajeRegistrada
        );
        this.editandoId.set(null);
        this.reiniciarFormulario();
        this.isSubmitting.set(false);

        // El listado va por vencimiento más próximo, así que lo nuevo no necesariamente queda
        // arriba: se vuelve a la primera página en vez de adivinar dónde cayó. Al editar se
        // respeta la página actual, donde está la fila que tocaste.
        if (idEditando === null) {
          this.pagina.set(1);
        }
        this.cargarDocumentos();
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }

  /**
   * Para rebajar un documento sin dejar respaldo tributario se edita el monto total. Si hace
   * falta respaldo, se emite una nota de crédito con "Nota" en vez de editar.
   */
  protected editar(documento: Documento): void {
    this.editandoId.set(documento.id);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.form.setValue({
      idTercero: documento.idTercero,
      tipoDocumento: documento.tipoDocumento,
      idDocumentoReferencia: null,
      numeroDocumento: documento.numeroDocumento ?? '',
      montoTotal: documento.montoTotal,
      montoNeto: documento.montoNeto,
      montoIva: documento.montoIva,
      fechaEmision: documento.fechaEmision,
      fechaVencimiento: documento.fechaVencimiento,
      idCategoria: documento.idCategoria,
      descripcion: documento.descripcion ?? ''
    });
  }

  /** Abre el formulario en modo nota, ya apuntando al documento de la fila. */
  protected emitirNota(documento: Documento): void {
    this.editandoId.set(null);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const notaCredito = this.tiposDocumento().find((t) => t.esNota && t.signo < 0);

    this.form.setValue({
      idTercero: null,
      tipoDocumento: notaCredito?.valor ?? '',
      idDocumentoReferencia: documento.id,
      numeroDocumento: '',
      montoTotal: null,
      montoNeto: null,
      montoIva: 0,
      fechaEmision: hoy(),
      fechaVencimiento: this.vencimientoPropuesto(),
      idCategoria: null,
      descripcion: ''
    });
  }

  protected cancelarEdicion(): void {
    this.editandoId.set(null);
    this.reiniciarFormulario();
  }

  protected verNotas(documento: Documento): void {
    this.errorMessage.set(null);

    this.documentosService.listNotas(documento.id).subscribe({
      next: (notas) => this.notasVisibles.set({ documento, notas }),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });
  }

  protected cerrarNotas(): void {
    this.notasVisibles.set(null);
  }

  // ----- Cobros y pagos -----------------------------------------------------

  /**
   * Cobrar o pagar no crea plata: enlaza un movimiento de caja que ya existe con el documento
   * que cubre. Por eso se elige de una lista en vez de escribir un monto suelto.
   */
  protected abrirAplicar(documento: Documento): void {
    this.aplicando.set(documento);
    this.errorAplicacion.set(null);
    this.cargandoAplicacion.set(true);
    this.formAplicar.reset({ idMovimiento: null, montoAplicado: documento.saldoPendiente });

    this.documentosService.listMovimientosAplicables(this.tipoCuenta).subscribe({
      next: (movimientos) => {
        this.movimientosAplicables.set(movimientos);
        this.cargandoAplicacion.set(false);
      },
      error: (error: unknown) => {
        this.errorAplicacion.set(mensajeDeError(error));
        this.cargandoAplicacion.set(false);
      }
    });

    this.documentosService.listPagos(documento.id).subscribe({
      next: (pagos) => this.pagosDelDocumento.set(pagos),
      error: (error: unknown) => this.errorAplicacion.set(mensajeDeError(error))
    });
  }

  protected cerrarAplicar(): void {
    this.aplicando.set(null);
    this.movimientosAplicables.set([]);
    this.pagosDelDocumento.set([]);
    this.errorAplicacion.set(null);
  }

  protected registrarAplicacion(): void {
    const documento = this.aplicando();
    if (!documento || this.formAplicar.invalid) {
      this.formAplicar.markAllAsTouched();
      return;
    }

    const { idMovimiento, montoAplicado } = this.formAplicar.getRawValue();
    this.cargandoAplicacion.set(true);
    this.errorAplicacion.set(null);

    this.documentosService.crearPago(documento.id, idMovimiento!, montoAplicado!).subscribe({
      next: () => {
        this.cargandoAplicacion.set(false);
        this.successMessage.set(this.pantalla()?.mensajeAplicado ?? null);
        this.cerrarAplicar();
        this.cargarDocumentos();
      },
      error: (error: unknown) => {
        this.errorAplicacion.set(mensajeDeError(error));
        this.cargandoAplicacion.set(false);
      }
    });
  }

  /** Quita el enlace. El movimiento de caja y el documento quedan los dos intactos. */
  protected quitarPago(pago: Pago): void {
    this.cargandoAplicacion.set(true);
    this.errorAplicacion.set(null);

    this.documentosService.eliminarPago(pago.id).subscribe({
      next: () => {
        this.cargandoAplicacion.set(false);
        this.successMessage.set(this.pantalla()?.mensajeQuitado ?? null);
        this.cerrarAplicar();
        this.cargarDocumentos();
      },
      error: (error: unknown) => {
        this.errorAplicacion.set(mensajeDeError(error));
        this.cargandoAplicacion.set(false);
      }
    });
  }

  // ----- Anulación ----------------------------------------------------------

  /** Anular es irreversible desde la app, así que se pregunta antes. */
  protected pedirAnulacion(documento: Documento): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.confirmandoAnulacion.set(documento);
  }

  protected cancelarConfirmacion(): void {
    this.confirmandoAnulacion.set(null);
  }

  protected confirmarAnulacion(): void {
    const pendiente = this.confirmandoAnulacion();
    if (!pendiente) {
      return;
    }

    this.anulandoId.set(pendiente.id);
    this.confirmandoAnulacion.set(null);

    this.documentosService.anular(pendiente.id).subscribe({
      next: () => {
        this.anulandoId.set(null);
        this.successMessage.set(
          this.esNota(pendiente.tipoDocumento)
            ? 'Nota anulada. El documento que corregía recuperó ese monto.'
            : (this.pantalla()?.mensajeAnulada ?? null)
        );

        // Si se estaba editando justo el que se anuló, el formulario apuntaría a algo que el
        // backend ya no acepta: se sale de edición en vez de dejar escribir para nada.
        if (this.editandoId() === pendiente.id) {
          this.cancelarEdicion();
        }

        this.cargarDocumentos();
      },
      error: (error: unknown) => {
        this.anulandoId.set(null);
        this.errorMessage.set(mensajeDeError(error));
      }
    });
  }

  // ----- Paginación ---------------------------------------------------------

  protected paginaAnterior(): void {
    if (this.pagina() > 1) {
      this.pagina.update((p) => p - 1);
      this.cargarDocumentos();
    }
  }

  protected paginaSiguiente(): void {
    if (this.pagina() < this.totalPaginas()) {
      this.pagina.update((p) => p + 1);
      this.cargarDocumentos();
    }
  }

  private cargarCorregibles(): void {
    this.documentosService.listCorregibles(this.tipoCuenta).subscribe({
      next: (documentos) => this.corregibles.set(documentos),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });
  }

  private cargarDocumentos(): void {
    this.isLoading.set(true);

    this.documentosService
      .listDocumentos({
        tipoCuenta: this.tipoCuenta,
        pagina: this.pagina(),
        tamanoPagina: this.tamanoPagina
      })
      .subscribe({
        next: (resultado) => {
          this.totalResultados.set(resultado.totalResultados);

          // La página pedida puede quedar fuera de rango si el total bajó: se retrocede a la
          // última con resultados para no dejar un listado vacío sin controles. Retrocede una
          // sola vez, porque ultimaPagina ya es válida.
          const ultimaPagina = Math.max(1, Math.ceil(resultado.totalResultados / this.tamanoPagina));
          if (resultado.items.length === 0 && this.pagina() > ultimaPagina) {
            this.pagina.set(ultimaPagina);
            this.cargarDocumentos();
            return;
          }

          this.documentos.set(resultado.items);
          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.errorMessage.set(mensajeDeError(error));
          this.isLoading.set(false);
        }
      });
  }

  private reiniciarFormulario(): void {
    this.form.reset({
      idTercero: null,
      tipoDocumento: this.tiposDocumento().find((t) => !t.esNota)?.valor ?? '',
      idDocumentoReferencia: null,
      numeroDocumento: '',
      montoTotal: null,
      montoNeto: null,
      montoIva: 0,
      fechaEmision: hoy(),
      fechaVencimiento: this.vencimientoPropuesto(),
      idCategoria: null,
      descripcion: ''
    });
  }
}
