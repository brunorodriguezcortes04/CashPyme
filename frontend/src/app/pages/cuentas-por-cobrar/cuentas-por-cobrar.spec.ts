import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { Documento } from '../../core/documentos.service';
import { CuentasPorCobrar } from './cuentas-por-cobrar';

/**
 * El formulario y submit() son protected para que solo los use la plantilla. La prueba los
 * alcanza por este tipo en vez de abrirlos en el componente: el encapsulamiento se mantiene
 * y la prueba sigue leyendo lo que de verdad ejecuta la pantalla.
 */
type Interno = {
  form: FormGroup;
  formCobro: FormGroup;
  submit(): void;
  editar(documento: Documento): void;
  emitirNota(documento: Documento): void;
  pedirAnulacion(documento: Documento): void;
  confirmarAnulacion(): void;
  abrirCobro(documento: Documento): void;
  registrarCobro(): void;
  editandoId: Signal<number | null>;
};

function documentoDe(sobrescribe: Partial<Documento> = {}): Documento {
  const montoTotal = sobrescribe.montoTotal ?? 119000;
  return {
    id: 1,
    tipoCuenta: 'por_cobrar',
    tipoDocumento: 'factura',
    numeroDocumento: '1045',
    idTercero: CLIENTE.id,
    nombreTercero: CLIENTE.nombreRazonSocial,
    idCategoria: null,
    nombreCategoria: null,
    montoNeto: null,
    montoIva: 0,
    montoTotal,
    fechaEmision: enDias(0),
    fechaVencimiento: enDias(30),
    estadoDocumento: 'pendiente',
    descripcion: null,
    fechaCreacion: new Date().toISOString(),
    montoPagado: 0,
    montoNotasCredito: 0,
    montoNotasDebito: 0,
    saldoPendiente: montoTotal,
    estaVencido: false,
    diasAtraso: 0,
    idDocumentoReferencia: null,
    ...sobrescribe
  };
}

function pagina(items: Documento[]) {
  return { items, totalResultados: items.length, pagina: 1, tamanoPagina: 20 };
}

const CLIENTE = {
  id: 7,
  tipoTercero: 'cliente',
  nombreRazonSocial: 'Distribuidora Los Andes SpA',
  rut: null,
  giro: null,
  email: null,
  telefono: null,
  direccion: null,
  activo: true
};

function enDias(dias: number): string {
  const fecha = new Date();
  fecha.setDate(fecha.getDate() + dias);
  const mes = String(fecha.getMonth() + 1).padStart(2, '0');
  const dia = String(fecha.getDate()).padStart(2, '0');
  return `${fecha.getFullYear()}-${mes}-${dia}`;
}

describe('CuentasPorCobrar', () => {
  let fixture: ComponentFixture<CuentasPorCobrar>;
  let interno: Interno;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CuentasPorCobrar],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(CuentasPorCobrar);
    interno = fixture.componentInstance as unknown as Interno;
    http = TestBed.inject(HttpTestingController);

    responderCargaInicial();
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  /** Las cuatro llamadas que hace el constructor: catálogo, clientes, categorías y listado. */
  function responderCargaInicial(documentos: unknown[] = []): void {
    http.expectOne((r) => r.url === '/api/opciones/tipos-documento').flush([
      { valor: 'factura', etiqueta: 'Factura', esNota: false, signo: 1 },
      { valor: 'boleta', etiqueta: 'Boleta', esNota: false, signo: 1 },
      { valor: 'nota_credito', etiqueta: 'Nota de crédito', esNota: true, signo: -1 },
      { valor: 'nota_debito', etiqueta: 'Nota de débito', esNota: true, signo: 1 }
    ]);
    http.expectOne((r) => r.url === '/api/terceros').flush([CLIENTE]);
    http.expectOne((r) => r.url === '/api/categorias').flush([]);
    http.expectOne((r) => r.url === '/api/documentos').flush({
      items: documentos,
      totalResultados: documentos.length,
      pagina: 1,
      tamanoPagina: 20
    });
  }

  it('deja el vencimiento a 30 días por defecto', () => {
    expect(interno.form.getRawValue().fechaVencimiento).toBe(enDias(30));
  });

  // Criterio de aceptación 1: cliente y monto válidos, vencimiento a 30 días → 'pendiente'.
  it('registra la cuenta por cobrar y muestra el estado pendiente que devuelve el backend', () => {
    interno.form.patchValue({ idTercero: CLIENTE.id, montoTotal: 119000 });
    interno.submit();

    const peticion = http.expectOne(
      (r) => r.method === 'POST' && r.url === '/api/documentos'
    );

    expect(peticion.request.body).toMatchObject({
      tipoCuenta: 'por_cobrar',
      tipoDocumento: 'factura',
      idTercero: CLIENTE.id,
      montoTotal: 119000,
      fechaVencimiento: enDias(30)
    });

    peticion.flush(documentoDe({ numeroDocumento: null }));

    // Tras crear se recarga el listado desde la página 1.
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos')
      .flush(pagina([documentoDe({ numeroDocumento: null })]));

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Pendiente');
    expect(fixture.nativeElement.textContent).toContain('Cuenta por cobrar registrada.');
  });

  // Criterio de aceptación 2: vencimiento anterior a la emisión → se rechaza.
  it('rechaza un vencimiento anterior a la emisión sin llegar al backend', () => {
    interno.form.patchValue({
      idTercero: CLIENTE.id,
      montoTotal: 50000,
      fechaEmision: enDias(10),
      fechaVencimiento: enDias(5)
    });

    interno.submit();

    expect(interno.form.hasError('vencimientoAnterior')).toBe(true);
    // Nada sale a la red: afterEach llama a http.verify() y fallaría si hubiera un POST.
    http.expectNone((r) => r.method === 'POST');

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'La fecha de vencimiento no puede ser anterior a la fecha de emisión.'
    );
  });

  it('rechaza un desglose donde neto más IVA no da el total', () => {
    interno.form.patchValue({
      idTercero: CLIENTE.id,
      montoTotal: 119000,
      montoNeto: 100000,
      montoIva: 10000
    });

    interno.submit();

    expect(interno.form.hasError('montosNoCuadran')).toBe(true);
    http.expectNone((r) => r.method === 'POST');
  });

  it('acepta el desglose cuando neto más IVA da exactamente el total', () => {
    interno.form.patchValue({
      idTercero: CLIENTE.id,
      montoTotal: 119000,
      montoNeto: 100000,
      montoIva: 19000
    });

    expect(interno.form.hasError('montosNoCuadran')).toBe(false);
    expect(interno.form.valid).toBe(true);
  });

  it('al editar carga la cuenta en el formulario y guarda con PUT', () => {
    interno.editar(documentoDe({ id: 42, descripcion: 'Venta original' }));

    expect(interno.editandoId()).toBe(42);
    expect(interno.form.getRawValue()).toMatchObject({
      idTercero: CLIENTE.id,
      tipoDocumento: 'factura',
      numeroDocumento: '1045',
      montoTotal: 119000,
      descripcion: 'Venta original'
    });

    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/documentos/42');
    // El tipo de cuenta no viaja al editar: no se puede cambiar.
    expect(peticion.request.body.tipoCuenta).toBeUndefined();
    peticion.flush(documentoDe({ id: 42 }));

    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush({
      items: [documentoDe({ id: 42 })],
      totalResultados: 1,
      pagina: 1,
      tamanoPagina: 20
    });

    expect(interno.editandoId()).toBeNull();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Cambios guardados.');
  });

  // La rebaja parcial (devolución, descuento posterior) se hace bajando el monto total:
  // saldo_pendiente = monto_total - pagado, así que el saldo por cobrar baja solo.
  it('manda el monto rebajado al editar, que es cómo se refleja una devolución', () => {
    interno.editar(documentoDe({ id: 42, montoTotal: 119000 }));
    interno.form.patchValue({ montoTotal: 59500 });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/documentos/42');
    expect(peticion.request.body.montoTotal).toBe(59500);
    peticion.flush(documentoDe({ id: 42, montoTotal: 59500 }));

    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush({
      items: [documentoDe({ id: 42, montoTotal: 59500 })],
      totalResultados: 1,
      pagina: 1,
      tamanoPagina: 20
    });
  });

  it('pide confirmación antes de anular y solo entonces llama al backend', () => {
    const documento = documentoDe({ id: 42 });
    interno.pedirAnulacion(documento);

    // Mientras no se confirma, nada sale a la red.
    http.expectNone((r) => r.url.includes('/anular'));

    interno.confirmarAnulacion();
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos/42/anular')
      .flush(documentoDe({ id: 42, estadoDocumento: 'anulado' }));

    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush({
      items: [documentoDe({ id: 42, estadoDocumento: 'anulado' })],
      totalResultados: 1,
      pagina: 1,
      tamanoPagina: 20
    });

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Cuenta por cobrar anulada.');
    expect(fixture.nativeElement.textContent).toContain('Anulada');
  });

  it('muestra el motivo cuando el backend rechaza la anulación por tener pagos', () => {
    interno.pedirAnulacion(documentoDe({ id: 42 }));
    interno.confirmarAnulacion();

    http.expectOne((r) => r.url === '/api/documentos/42/anular').flush(
      { message: 'Este documento tiene 1 pago registrado. Anula primero ese movimiento de caja y después el documento.' },
      { status: 409, statusText: 'Conflict' }
    );

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Anula primero ese movimiento de caja');
  });

  it('sale de edición si se anula justo la cuenta que se estaba editando', () => {
    const documento = documentoDe({ id: 42 });
    interno.editar(documento);
    expect(interno.editandoId()).toBe(42);

    interno.pedirAnulacion(documento);
    interno.confirmarAnulacion();
    http.expectOne((r) => r.url === '/api/documentos/42/anular')
      .flush(documentoDe({ id: 42, estadoDocumento: 'anulado' }));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush({
      items: [],
      totalResultados: 0,
      pagina: 1,
      tamanoPagina: 20
    });

    expect(interno.editandoId()).toBeNull();
    expect(interno.form.getRawValue().montoTotal).toBeNull();
  });

  // ----- Notas de crédito y débito -----------------------------------------

  it('al emitir una nota desde una fila queda elegido el documento que corrige', () => {
    interno.emitirNota(documentoDe({ id: 42 }));

    expect(interno.form.getRawValue()).toMatchObject({
      tipoDocumento: 'nota_credito',
      idDocumentoReferencia: 42,
      idTercero: CLIENTE.id
    });
    expect(interno.form.hasError('referenciaRequerida')).toBe(false);
  });

  it('exige el documento que corrige y no manda la nota sin él', () => {
    interno.form.patchValue({
      idTercero: CLIENTE.id,
      tipoDocumento: 'nota_credito',
      montoTotal: 40000,
      idDocumentoReferencia: null
    });

    interno.submit();

    expect(interno.form.hasError('referenciaRequerida')).toBe(true);
    http.expectNone((r) => r.method === 'POST');
  });

  it('manda la nota con su referencia y el vencimiento igual a la emisión', () => {
    interno.emitirNota(documentoDe({ id: 42 }));
    interno.form.patchValue({ montoTotal: 40000, numeroDocumento: 'NC-1' });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos');
    // Una nota no tiene vencimiento propio: va la fecha de emisión para cumplir ck_doc_fechas.
    expect(peticion.request.body).toMatchObject({
      tipoDocumento: 'nota_credito',
      idDocumentoReferencia: 42,
      montoTotal: 40000,
      fechaEmision: enDias(0),
      fechaVencimiento: enDias(0)
    });

    peticion.flush(documentoDe({
      id: 50, tipoDocumento: 'nota_credito', montoTotal: 40000,
      saldoPendiente: 0, idDocumentoReferencia: 42
    }));

    // El documento que corrige vuelve con el saldo ya rebajado.
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(
      pagina([documentoDe({ id: 42, montoNotasCredito: 40000, saldoPendiente: 79000 })])
    );

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Nota emitida.');
    // El separador de miles depende del locale del entorno (coma acá, punto en la app), así que
    // la aserción acepta los dos en vez de atarse a uno.
    expect(fixture.nativeElement.textContent).toMatch(/79[.,]000/);
    expect(fixture.nativeElement.textContent).toMatch(/−40[.,]000/);
  });

  it('un documento normal no manda referencia aunque haya quedado una elegida', () => {
    interno.emitirNota(documentoDe({ id: 42 }));
    interno.form.patchValue({ tipoDocumento: 'factura', montoTotal: 10000 });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos');
    expect(peticion.request.body.idDocumentoReferencia).toBeNull();
    peticion.flush(documentoDe({ id: 60 }));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(pagina([]));
  });

  // ----- Cobros -------------------------------------------------------------

  it('aplica un movimiento al documento y recarga el listado', () => {
    const documento = documentoDe({ id: 42, saldoPendiente: 84000 });
    interno.abrirCobro(documento);

    http.expectOne((r) => r.url === '/api/pagos/movimientos-aplicables').flush([
      {
        id: 34, fecha: enDias(0), monto: 84000, montoDisponible: 84000,
        medioPago: 'transferencia', nombreCuenta: 'Caja', descripcion: 'Cobro factura'
      }
    ]);
    http.expectOne((r) => r.url === '/api/pagos/documento/42').flush([]);

    // El monto se precarga con el saldo, que es el caso normal: cobro completo.
    expect(interno.formCobro.getRawValue().montoAplicado).toBe(84000);

    interno.formCobro.patchValue({ idMovimiento: 34 });
    interno.registrarCobro();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/pagos');
    expect(peticion.request.body).toEqual({ idDocumento: 42, idMovimiento: 34, montoAplicado: 84000 });
    peticion.flush({
      id: 1, idDocumento: 42, idMovimiento: 34, montoAplicado: 84000,
      fechaMovimiento: enDias(0), medioPago: 'transferencia', descripcionMovimiento: 'Cobro factura'
    });

    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(
      pagina([documentoDe({ id: 42, montoPagado: 84000, saldoPendiente: 0, estadoDocumento: 'pagado' })])
    );

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Cobro registrado.');
    expect(fixture.nativeElement.textContent).toContain('Pagada');
  });

  it('muestra el motivo cuando el cobro se pasa del saldo', () => {
    interno.abrirCobro(documentoDe({ id: 42, saldoPendiente: 84000 }));
    http.expectOne((r) => r.url === '/api/pagos/movimientos-aplicables').flush([
      {
        id: 34, fecha: enDias(0), monto: 100000, montoDisponible: 100000,
        medioPago: 'transferencia', nombreCuenta: 'Caja', descripcion: null
      }
    ]);
    http.expectOne((r) => r.url === '/api/pagos/documento/42').flush([]);

    interno.formCobro.patchValue({ idMovimiento: 34, montoAplicado: 100000 });
    interno.registrarCobro();

    http.expectOne((r) => r.url === '/api/pagos').flush(
      { message: 'El pago no puede pasar de 84.000, que es el saldo que queda del documento.' },
      { status: 409, statusText: 'Conflict' }
    );

    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('El pago no puede pasar de 84.000');
  });

  it('un documento cerrado solo por notas se muestra distinto de uno cobrado', () => {
    const cerradoPorNota = documentoDe({
      id: 42, estadoDocumento: 'pagado', montoPagado: 0,
      montoNotasCredito: 119000, saldoPendiente: 0
    });

    expect((interno as unknown as { etiquetaEstado(d: Documento): string })
      .etiquetaEstado(cerradoPorNota)).toBe('Cerrada por nota');

    expect((interno as unknown as { etiquetaEstado(d: Documento): string })
      .etiquetaEstado(documentoDe({ estadoDocumento: 'pagado', montoPagado: 119000 }))).toBe('Pagada');
  });
});
