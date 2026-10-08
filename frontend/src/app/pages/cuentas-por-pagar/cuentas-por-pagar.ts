import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { mensajeDeError } from '../../core/api-error';
import { CatalogosService, TipoDocumento } from '../../core/catalogos.service';
import {
  Documento,
  DocumentosService,
  MovimientoAplicable,
  Pago,
  TIPO_CUENTA_POR_PAGAR
} from '../../core/documentos.service';
import { Categoria, MovimientosService } from '../../core/movimientos.service';
import { Tercero, TercerosService } from '../../core/terceros.service';

const DIAS_VENCIMIENTO_POR_DEFECTO = 30;

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

function vencimientoNoAnteriorAEmision(control: AbstractControl): ValidationErrors | null {
  const emision = control.get('fechaEmision')?.value;
  const vencimiento = control.get('fechaVencimiento')?.value;
  return emision && vencimiento && vencimiento < emision ? { vencimientoAnterior: true } : null;
}

function montosCuadran(control: AbstractControl): ValidationErrors | null {
  const neto = control.get('montoNeto')?.value;
  const total = control.get('montoTotal')?.value;

  if (neto === null || neto === '' || total === null || total === '') {
    return null;
  }

  const iva = Number(control.get('montoIva')?.value ?? 0);
  return Math.abs(Number(neto) + iva - Number(total)) > 0.005 ? { montosNoCuadran: true } : null;
}

@Component({
  selector: 'app-cuentas-por-pagar',
  imports: [ReactiveFormsModule, DecimalPipe, RouterLink],
  templateUrl: './cuentas-por-pagar.html',
  styleUrl: './cuentas-por-pagar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CuentasPorPagar {
  private readonly formBuilder = inject(FormBuilder);
  private readonly documentosService = inject(DocumentosService);
  private readonly tercerosService = inject(TercerosService);
  private readonly movimientosService = inject(MovimientosService);
  private readonly catalogosService = inject(CatalogosService);

  protected readonly tiposDocumento = signal<TipoDocumento[]>([]);
  protected readonly categorias = signal<Categoria[]>([]);
  protected readonly documentos = signal<Documento[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  /** Solo proveedores (y 'ambos'): una cuenta por pagar es contra alguien a quien le compras. */
  private readonly terceros = signal<Tercero[]>([]);
  protected readonly proveedores = computed(() =>
    this.terceros().filter((t) => t.tipoTercero === 'proveedor' || t.tipoTercero === 'ambos')
  );
  protected readonly hasProveedores = computed(() => this.proveedores().length > 0);

  protected readonly corregibles = computed(() =>
    this.documentos().filter((d) => !this.esNota(d.tipoDocumento) && d.estadoDocumento !== 'anulado')
  );

  protected readonly editandoId = signal<number | null>(null);
  protected readonly estaEditando = computed(() => this.editandoId() !== null);

  protected readonly confirmandoAnulacion = signal<Documento | null>(null);
  protected readonly anulandoId = signal<number | null>(null);

  // Los nombres "cobrando / abrirCobro..." se dejaron iguales a los de cuentas por cobrar
  // para que el HTML solo cambie en textos. Aquí "cobro" significa "pago".
  protected readonly cobrando = signal<Documento | null>(null);
  protected readonly movimientosAplicables = signal<MovimientoAplicable[]>([]);
  protected readonly pagosDelDocumento = signal<Pago[]>([]);
  protected readonly cargandoCobro = signal(false);
  protected readonly errorCobro = signal<string | null>(null);

  protected readonly notasVisibles = signal<{ documento: Documento; notas: Documento[] } | null>(null);

  private readonly tamanoPagina = 20;
  protected readonly pagina = signal(1);
  protected readonly totalResultados = signal(0);
  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.totalResultados() / this.tamanoPagina))
  );

  protected readonly form = this.formBuilder.nonNullable.group(
    {
      idTercero: [null as number | null, [Validators.required]],
      tipoDocumento: ['', [Validators.required]],
      idDocumentoReferencia: [null as number | null],
      numeroDocumento: ['', [Validators.maxLength(50)]],
      montoTotal: [null as number | null, [Validators.required, Validators.min(0.01)]],
      montoNeto: [null as number | null, [Validators.min(0)]],
      montoIva: [0, [Validators.min(0)]],
      fechaEmision: [hoy(), [Validators.required]],
      fechaVencimiento: [enDias(DIAS_VENCIMIENTO_POR_DEFECTO), [Validators.required]],
      idCategoria: [null as number | null],
      descripcion: ['', [Validators.maxLength(250)]]
    },
    {
      validators: [
        vencimientoNoAnteriorAEmision,
        montosCuadran,
        (control: AbstractControl) => this.referenciaCoherente(control)
      ]
    }
  );

  protected readonly formCobro = this.formBuilder.nonNullable.group({
    idMovimiento: [null as number | null, [Validators.required]],
    montoAplicado: [null as number | null, [Validators.required, Validators.min(0.01)]]
  });

  constructor() {
    this.catalogosService.listTiposDocumento().subscribe({
      next: (tipos) => {
        this.tiposDocumento.set(tipos);
        this.form.controls.tipoDocumento.setValue(tipos.find((t) => !t.esNota)?.valor ?? '');
      },
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.tercerosService.listTerceros().subscribe({
      next: (terceros) => this.terceros.set(terceros),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    // Una cuenta por pagar termina siendo un egreso de caja: su categoría tiene que ser de egreso.
    this.movimientosService.listCategorias('egreso').subscribe({
      next: (categorias) => this.categorias.set(categorias),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.cargarDocumentos();
  }

  protected esNota(tipoDocumento: string): boolean {
    return this.tiposDocumento().find((t) => t.valor === tipoDocumento)?.esNota ?? false;
  }

  protected notaEnCurso(): boolean {
    return this.esNota(this.form.controls.tipoDocumento.value);
  }

  protected etiquetaTipoDocumento(valor: string): string {
    return this.tiposDocumento().find((t) => t.valor === valor)?.etiqueta ?? valor;
  }

  protected etiquetaEstado(documento: Documento): string {
    if (this.esNota(documento.tipoDocumento)) {
      return documento.estadoDocumento === 'anulado' ? 'Anulada' : 'Vigente';
    }

    switch (documento.estadoDocumento) {
      case 'pendiente':
        return 'Pendiente';
      case 'pagado_parcial':
        return 'Pago parcial';
      case 'pagado':
        return documento.montoPagado > 0 ? 'Pagada' : 'Cerrada por nota';
      case 'anulado':
        return 'Anulada';
      default:
        return documento.estadoDocumento;
    }
  }

  protected tieneNotas(documento: Documento): boolean {
    return documento.montoNotasCredito > 0 || documento.montoNotasDebito > 0;
  }

  private referenciaCoherente(control: AbstractControl): ValidationErrors | null {
    const tipo = control.get('tipoDocumento')?.value;
    const referencia = control.get('idDocumentoReferencia')?.value;

    if (!this.esNota(tipo)) {
      return null;
    }

    return referencia ? null : { referenciaRequerida: true };
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const valores = this.form.getRawValue();
    const esNota = this.esNota(valores.tipoDocumento);

    const payload = {
      tipoDocumento: valores.tipoDocumento,
      idTercero: valores.idTercero!,
      montoTotal: valores.montoTotal!,
      fechaEmision: valores.fechaEmision,
      fechaVencimiento: esNota ? valores.fechaEmision : valores.fechaVencimiento,
      numeroDocumento: valores.numeroDocumento || null,
      idCategoria: valores.idCategoria,
      montoNeto: valores.montoNeto,
      montoIva: valores.montoIva,
      descripcion: valores.descripcion || null,
      idDocumentoReferencia: esNota ? valores.idDocumentoReferencia : null
    };

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const idEditando = this.editandoId();
    const peticion = idEditando === null
      ? this.documentosService.crear({ ...payload, tipoCuenta: TIPO_CUENTA_POR_PAGAR })
      : this.documentosService.actualizar(idEditando, payload);

    peticion.subscribe({
      next: () => {
        this.successMessage.set(
          idEditando !== null
            ? 'Cambios guardados.'
            : esNota
              ? 'Nota emitida. El saldo del documento que corrige ya quedó ajustado.'
              : 'Cuenta por pagar registrada.'
        );
        this.editandoId.set(null);
        this.reiniciarFormulario();
        this.isSubmitting.set(false);

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

  protected editar(documento: Documento): void {
    this.editandoId.set(documento.id);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.form.setValue({
      idTercero: documento.idTercero,
      tipoDocumento: documento.tipoDocumento,
      idDocumentoReferencia: documento.idDocumentoReferencia,
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

  protected emitirNota(documento: Documento): void {
    this.editandoId.set(null);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const notaCredito = this.tiposDocumento().find((t) => t.valor === 'nota_credito');

    this.form.setValue({
      idTercero: documento.idTercero,
      tipoDocumento: notaCredito?.valor ?? '',
      idDocumentoReferencia: documento.id,
      numeroDocumento: '',
      montoTotal: null,
      montoNeto: null,
      montoIva: 0,
      fechaEmision: hoy(),
      fechaVencimiento: hoy(),
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

  // ----- Pagos (en el código se llaman "cobro") -----------------------------

  protected abrirCobro(documento: Documento): void {
    this.cobrando.set(documento);
    this.errorCobro.set(null);
    this.cargandoCobro.set(true);
    this.formCobro.reset({ idMovimiento: null, montoAplicado: documento.saldoPendiente });

    this.documentosService.listMovimientosAplicables(TIPO_CUENTA_POR_PAGAR).subscribe({
      next: (movimientos) => {
        this.movimientosAplicables.set(movimientos);
        this.cargandoCobro.set(false);
      },
      error: (error: unknown) => {
        this.errorCobro.set(mensajeDeError(error));
        this.cargandoCobro.set(false);
      }
    });

    this.documentosService.listPagos(documento.id).subscribe({
      next: (pagos) => this.pagosDelDocumento.set(pagos),
      error: (error: unknown) => this.errorCobro.set(mensajeDeError(error))
    });
  }

  protected cerrarCobro(): void {
    this.cobrando.set(null);
    this.movimientosAplicables.set([]);
    this.pagosDelDocumento.set([]);
    this.errorCobro.set(null);
  }

  protected registrarCobro(): void {
    const documento = this.cobrando();
    if (!documento || this.formCobro.invalid) {
      this.formCobro.markAllAsTouched();
      return;
    }

    const { idMovimiento, montoAplicado } = this.formCobro.getRawValue();
    this.cargandoCobro.set(true);
    this.errorCobro.set(null);

    this.documentosService.crearPago(documento.id, idMovimiento!, montoAplicado!).subscribe({
      next: () => {
        this.cargandoCobro.set(false);
        this.successMessage.set('Pago registrado.');
        this.cerrarCobro();
        this.cargarDocumentos();
      },
      error: (error: unknown) => {
        this.errorCobro.set(mensajeDeError(error));
        this.cargandoCobro.set(false);
      }
    });
  }

  protected quitarPago(pago: Pago): void {
    this.cargandoCobro.set(true);
    this.errorCobro.set(null);

    this.documentosService.eliminarPago(pago.id).subscribe({
      next: () => {
        this.cargandoCobro.set(false);
        this.pagosDelDocumento.update((pagos) => pagos.filter((p) => p.id !== pago.id));
        this.successMessage.set('Pago quitado.');
        this.cerrarCobro();
        this.cargarDocumentos();
      },
      error: (error: unknown) => {
        this.errorCobro.set(mensajeDeError(error));
        this.cargandoCobro.set(false);
      }
    });
  }

  // ----- Anulación ----------------------------------------------------------

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
            : 'Cuenta por pagar anulada.'
        );

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

  private cargarDocumentos(): void {
    this.isLoading.set(true);

    this.documentosService
      .listDocumentos({
        tipoCuenta: TIPO_CUENTA_POR_PAGAR,
        pagina: this.pagina(),
        tamanoPagina: this.tamanoPagina
      })
      .subscribe({
        next: (resultado) => {
          this.totalResultados.set(resultado.totalResultados);

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
      fechaVencimiento: enDias(DIAS_VENCIMIENTO_POR_DEFECTO),
      idCategoria: null,
      descripcion: ''
    });
  }
}