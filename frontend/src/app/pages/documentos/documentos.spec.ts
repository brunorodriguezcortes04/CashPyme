import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TipoCuentaDocumento } from '../../core/catalogos.service';
import { Documento } from '../../core/documentos.service';
import { enDias } from '../../core/fechas';
import { Documentos } from './documentos';

/**
 * El formulario y submit() son protected para que solo los use la plantilla. La prueba los
 * alcanza por este tipo en vez de abrirlos en el componente: el encapsulamiento se mantiene
 * y la prueba sigue leyendo lo que de verdad ejecuta la pantalla.
 */
type Interno = {
  form: FormGroup;
  formAplicar: FormGroup;
  submit(): void;
  editar(documento: Documento): void;
  emitirNota(documento: Documento): void;
  pedirAnulacion(documento: Documento): void;
  confirmarAnulacion(): void;
  abrirAplicar(documento: Documento): void;
  registrarAplicacion(): void;
  editandoId: Signal<number | null>;
};

/** Lo que responde /api/opciones/tipos-cuenta-documento (los textos reales viven en el backend). */
function pantallaDe(valor: string, flujo: string, tercero: string, palabra: string): TipoCuentaDocumento {
  return {
    valor,
    titulo: `Cuentas por ${palabra}`,
    subtitulo: `Subtítulo de ${palabra}`,
    tituloNueva: `Nueva cuenta por ${palabra}`,
    tituloEditar: `Editar cuenta por ${palabra}`,
    botonRegistrar: `Registrar cuenta por ${palabra}`,
    tituloListado: `Cuentas por ${palabra} registradas`,
    mensajeVacio: `Todavía no registras ninguna cuenta por ${palabra}.`,
    mensajeRegistrada: `Cuenta por ${palabra} registrada.`,
    mensajeAnulada: `Cuenta por ${palabra} anulada.`,
    preguntaAnular: `¿Anular esta cuenta por ${palabra}?`,
    efectoAnular: `Deja de contar como plata por ${palabra}.`,
    tercero,
    seleccionarTercero: `Selecciona un ${tercero.toLowerCase()}`,
    sinTerceros: `Todavía no tienes ${tercero.toLowerCase()}s registrados.`,
    ayudaNota: 'Ayuda de nota',
    ejemploDescripcion: 'Ej: algo a 30 días',
    columnaAplicado: palabra === 'cobrar' ? 'Cobrado' : 'Pagado',
    accion: palabra === 'cobrar' ? 'Cobrar' : 'Pagar',
    tituloAplicar: palabra === 'cobrar' ? 'Registrar cobro' : 'Registrar pago',
    aplicadosPrevios: 'Ya aplicados',
    mensajeAplicado: palabra === 'cobrar' ? 'Cobro registrado.' : 'Pago registrado.',
    mensajeQuitado: 'Quitado.',
    flujo,
    seleccionarMovimiento: `Selecciona el ${flujo}`,
    sinMovimientos: `No hay ${flujo}s con monto libre.`,
    rutaMovimientos: `movimientos/${flujo}`,
    nombrePantallaMovimientos: flujo === 'ingreso' ? 'Ingresos' : 'Egresos',
    ruta: valor === 'por_cobrar' ? 'cuentas-por-cobrar' : 'cuentas-por-pagar',
    // Distinto de 30 a propósito: la prueba demuestra que el plazo viene del backend.
    diasVencimientoPorDefecto: PLAZO
  };
}

/** Plazo de vencimiento que manda el backend en la configuración de la pantalla. */
const PLAZO = 45;

const PANTALLAS = [
  pantallaDe('por_cobrar', 'ingreso', 'Cliente', 'cobrar'),
  pantallaDe('por_pagar', 'egreso', 'Proveedor', 'pagar')
];

const TERCERO = {
  id: 7,
  tipoTercero: 'ambos',
  nombreRazonSocial: 'Distribuidora Los Andes SpA',
  rut: null,
  giro: null,
  email: null,
  telefono: null,
  direccion: null,
  activo: true
};

function documentoDe(sobrescribe: Partial<Documento> = {}): Documento {
  const montoTotal = sobrescribe.montoTotal ?? 119000;
  return {
    id: 1,
    tipoCuenta: 'por_cobrar',
    tipoDocumento: 'factura',
    numeroDocumento: '1045',
    idTercero: TERCERO.id,
    nombreTercero: TERCERO.nombreRazonSocial,
    idCategoria: null,
    nombreCategoria: null,
    montoNeto: null,
    montoIva: 0,
    montoTotal,
    fechaEmision: enDias(0),
    fechaVencimiento: enDias(PLAZO),
    estadoDocumento: 'pendiente',
    etiquetaEstado: 'Pendiente',
    descripcion: null,
    fechaCreacion: new Date().toISOString(),
    montoPagado: 0,
    montoNotasCredito: 0,
    montoNotasDebito: 0,
    saldoPendiente: montoTotal,
    estaVencido: false,
    diasAtraso: 0,
    situacion: 'pendiente',
    idDocumentoReferencia: null,
    acciones: { puedeAplicar: true, puedeEmitirNota: true, puedeEditar: true, puedeAnular: true },
    ...sobrescribe
  };
}

function pagina(items: Documento[]) {
  return { items, totalResultados: items.length, pagina: 1, tamanoPagina: 20 };
}

const RESUMEN = [
  { situacion: 'vencida', etiqueta: 'Vencidas', cantidad: 2, saldoPendiente: 60000 },
  { situacion: 'pendiente', etiqueta: 'Pendientes', cantidad: 1, saldoPendiente: 50000 },
  { situacion: 'pagada', etiqueta: 'Pagadas', cantidad: 0, saldoPendiente: 0 }
];

function crearPantalla(tipoCuenta: string, terceros: unknown[] = [TERCERO], documentos: Documento[] = []) {
  TestBed.configureTestingModule({
    imports: [Documentos],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { snapshot: { data: { tipoCuenta } } } }
    ]
  });

  const fixture: ComponentFixture<Documentos> = TestBed.createComponent(Documentos);
  const http = TestBed.inject(HttpTestingController);
  const pantalla = PANTALLAS.find((p) => p.valor === tipoCuenta)!;

  // Lo que pide el constructor: textos de la pantalla, tipos de documento, terceros elegibles y
  // el listado. Las categorías se piden cuando llegan los textos (dependen de su flujo).
  http.expectOne((r) => r.url === '/api/opciones/tipos-cuenta-documento').flush(PANTALLAS);
  http.expectOne((r) => r.url === '/api/opciones/tipos-documento').flush([
    { valor: 'factura', etiqueta: 'Factura', esNota: false, signo: 1 },
    { valor: 'boleta', etiqueta: 'Boleta', esNota: false, signo: 1 },
    { valor: 'nota_credito', etiqueta: 'Nota de crédito', esNota: true, signo: -1 },
    { valor: 'nota_debito', etiqueta: 'Nota de débito', esNota: true, signo: 1 }
  ]);
  const pedidoTerceros = http.expectOne((r) => r.url === '/api/documentos/terceros');
  pedidoTerceros.flush(terceros);
  const pedidoCategorias = http.expectOne((r) => r.url === '/api/categorias');
  pedidoCategorias.flush([]);
  http.expectOne((r) => r.url === '/api/documentos/resumen').flush(RESUMEN);
  const pedidoListado = http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos');
  pedidoListado.flush(pagina(documentos));
  fixture.detectChanges();

  return {
    fixture,
    http,
    pantalla,
    interno: fixture.componentInstance as unknown as Interno,
    texto: () => fixture.nativeElement.textContent as string,
    parametros: {
      terceros: pedidoTerceros.request.params,
      categorias: pedidoCategorias.request.params,
      listado: pedidoListado.request.params
    }
  };
}

// Cada recarga del listado vuelve a pedir el resumen de los contadores; los tests que no miran
// los contadores no tienen por qué contestarlo uno por uno.
afterEach(() => {
  const http = TestBed.inject(HttpTestingController);
  http.match((r) => r.url === '/api/documentos/resumen').forEach((r) => r.flush(RESUMEN));
  http.verify();
});

// Lo que cambia entre las dos pantallas viene del backend: se prueba igual para las dos.
describe.each([
  { tipoCuenta: 'por_cobrar', flujo: 'ingreso' },
  { tipoCuenta: 'por_pagar', flujo: 'egreso' }
])('Documentos ($tipoCuenta)', ({ tipoCuenta, flujo }) => {
  it('pide todo con su tipo de cuenta y muestra los textos que manda el backend', () => {
    const { pantalla, texto, parametros } = crearPantalla(tipoCuenta);

    expect(parametros.terceros.get('tipoCuenta')).toBe(tipoCuenta);
    expect(parametros.listado.get('tipoCuenta')).toBe(tipoCuenta);
    // La categoría de un documento es del tipo del movimiento con que se paga.
    expect(parametros.categorias.get('tipo')).toBe(flujo);

    expect(texto()).toContain(pantalla.titulo);
    expect(texto()).toContain(pantalla.tituloNueva);
    expect(texto()).toContain(pantalla.tercero);
    expect(texto()).toContain(pantalla.mensajeVacio);
  });

  it('muestra los grupos del resumen y filtra el listado por el elegido', () => {
    const { fixture, http, texto } = crearPantalla(tipoCuenta);

    expect(texto()).toContain('Vencidas');
    expect(texto()).toContain('Pendientes');

    const botones = Array.from(
      fixture.nativeElement.querySelectorAll('.filtro-situacion button') as NodeListOf<HTMLButtonElement>
    );
    botones.find((b) => b.textContent?.includes('Vencidas'))!.click();

    http.expectOne((r) => r.url === '/api/documentos/resumen').flush(RESUMEN);
    const listado = http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos');
    expect(listado.request.params.get('situacion')).toBe('vencida');
    expect(listado.request.params.get('tipoCuenta')).toBe(tipoCuenta);
    listado.flush(pagina([]));
  });

  it('con dos filtros seguidos descarta el primero y lista el último elegido', () => {
    const { fixture, http, texto } = crearPantalla(tipoCuenta);

    const botones = Array.from(
      fixture.nativeElement.querySelectorAll('.filtro-situacion button') as NodeListOf<HTMLButtonElement>
    );
    botones.find((b) => b.textContent?.includes('Vencidas'))!.click();
    botones.find((b) => b.textContent?.includes('Pendientes'))!.click();

    // El pedido de las vencidas queda cancelado: su respuesta ya no puede pisar a las pendientes.
    const listados = http.match((r) => r.method === 'GET' && r.url === '/api/documentos');
    expect(listados.map((p) => [p.request.params.get('situacion'), p.cancelled]))
      .toEqual([['vencida', true], ['pendiente', false]]);
    const resumenes = http.match((r) => r.url === '/api/documentos/resumen');
    expect(resumenes.map((p) => p.cancelled)).toEqual([true, false]);

    resumenes[1].flush(RESUMEN);
    listados[1].flush(pagina([documentoDe({ tipoCuenta, numeroDocumento: 'PEND-7' })]));

    fixture.detectChanges();
    expect(texto()).toContain('PEND-7');
  });

  it('si el listado falla muestra el motivo y el filtro siguiente vuelve a pedirlo', () => {
    const { fixture, http, texto } = crearPantalla(tipoCuenta);
    const todas = fixture.nativeElement.querySelector('.filtro-situacion button') as HTMLButtonElement;

    todas.click();
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos')
      .flush({ message: 'No pudimos cargar el listado.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(texto()).toContain('No pudimos cargar el listado.');

    todas.click();
    // Las recargas de contadores que quedaron sin responder se cancelan al pedir la siguiente.
    http.match((r) => r.url === '/api/documentos/resumen').filter((p) => !p.cancelled).forEach((p) => p.flush(RESUMEN));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos')
      .flush(pagina([documentoDe({ tipoCuenta, numeroDocumento: 'OTRA-9' })]));

    fixture.detectChanges();
    expect(texto()).toContain('OTRA-9');
  });

  it('ofrece los terceros que devuelve el backend, sin filtrarlos', () => {
    const otro = { ...TERCERO, id: 8, nombreRazonSocial: 'Otro', tipoTercero: 'cliente' };
    const { fixture } = crearPantalla(tipoCuenta, [TERCERO, otro]);

    const opciones = fixture.nativeElement.querySelectorAll('#idTercero option:not([disabled])');
    expect(opciones.length).toBe(2);
  });

  it('registra con su tipo de cuenta y muestra el mensaje y el estado que devuelve el backend', () => {
    const { interno, http, fixture, pantalla, texto } = crearPantalla(tipoCuenta);

    interno.form.patchValue({ idTercero: TERCERO.id, montoTotal: 119000 });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos');
    expect(peticion.request.body).toMatchObject({
      tipoCuenta,
      tipoDocumento: 'factura',
      idTercero: TERCERO.id,
      montoTotal: 119000,
      fechaVencimiento: enDias(PLAZO),
      idDocumentoReferencia: null
    });
    peticion.flush(documentoDe({ tipoCuenta }));

    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos')
      .flush(pagina([documentoDe({ tipoCuenta, etiquetaEstado: 'Pendiente' })]));

    fixture.detectChanges();
    expect(texto()).toContain(pantalla.mensajeRegistrada);
    expect(texto()).toContain('Pendiente');
  });

  it('aplica un movimiento del flujo de la pantalla y avisa con su mensaje', () => {
    const { interno, http, fixture, pantalla, texto } = crearPantalla(tipoCuenta);
    interno.abrirAplicar(documentoDe({ id: 42, tipoCuenta, saldoPendiente: 84000 }));

    const aplicables = http.expectOne((r) => r.url === '/api/pagos/movimientos-aplicables');
    expect(aplicables.request.params.get('tipoCuenta')).toBe(tipoCuenta);
    aplicables.flush([
      {
        id: 34, fecha: enDias(0), monto: 84000, montoDisponible: 84000,
        medioPago: 'transferencia', nombreCuenta: 'Caja', descripcion: null
      }
    ]);
    http.expectOne((r) => r.url === '/api/pagos/documento/42').flush([]);

    // El monto se precarga con el saldo, que es el caso normal: se cubre completo.
    expect(interno.formAplicar.getRawValue().montoAplicado).toBe(84000);
    fixture.detectChanges();
    expect(texto()).toContain(pantalla.tituloAplicar);

    interno.formAplicar.patchValue({ idMovimiento: 34 });
    interno.registrarAplicacion();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/pagos');
    expect(peticion.request.body).toEqual({ idDocumento: 42, idMovimiento: 34, montoAplicado: 84000 });
    peticion.flush({
      id: 1, idDocumento: 42, idMovimiento: 34, montoAplicado: 84000,
      fechaMovimiento: enDias(0), medioPago: 'transferencia', descripcionMovimiento: null
    });
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(
      pagina([documentoDe({
        id: 42, tipoCuenta, montoPagado: 84000, saldoPendiente: 0,
        estadoDocumento: 'pagado', etiquetaEstado: 'Pagada'
      })])
    );

    fixture.detectChanges();
    expect(texto()).toContain(pantalla.mensajeAplicado);
    expect(texto()).toContain('Pagada');
  });

  it('sin terceros que sirvan avisa con el texto de la pantalla', () => {
    const { pantalla, texto } = crearPantalla(tipoCuenta, []);

    expect(texto()).toContain(pantalla.sinTerceros);
  });
});

describe('Documentos (formulario)', () => {
  it('propone el vencimiento con el plazo que manda el backend', () => {
    const { interno, texto } = crearPantalla('por_cobrar');

    expect(interno.form.getRawValue().fechaVencimiento).toBe(enDias(PLAZO));
    expect(texto()).toContain(`Por defecto, ${PLAZO} días desde hoy.`);
  });

  it('marca el tercero si falta, sin llegar al backend', () => {
    const { interno, http } = crearPantalla('por_cobrar');

    interno.form.patchValue({ montoTotal: 50000 });
    interno.submit();

    expect(interno.form.hasError('terceroRequerido')).toBe(true);
    http.expectNone((r) => r.method === 'POST');
  });

  // Las reglas que cruzan campos (vencimiento >= emisión, neto + IVA = total) las valida el
  // backend: el formulario manda lo que hay y muestra su mensaje junto al formulario.
  it('muestra el mensaje del backend cuando una regla entre campos no se cumple', () => {
    const { interno, http, fixture, texto } = crearPantalla('por_cobrar');

    interno.form.patchValue({
      idTercero: TERCERO.id,
      montoTotal: 50000,
      fechaEmision: enDias(10),
      fechaVencimiento: enDias(5)
    });
    interno.submit();

    http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos').flush(
      { message: 'La fecha de vencimiento no puede ser anterior a la fecha de emisión.' },
      { status: 400, statusText: 'Bad Request' }
    );

    fixture.detectChanges();
    expect(texto()).toContain('La fecha de vencimiento no puede ser anterior a la fecha de emisión.');
  });

  it('muestra en cada fila solo las acciones que permite el backend', () => {
    const { fixture } = crearPantalla('por_cobrar', [TERCERO], [
      documentoDe({ id: 1 }),
      documentoDe({
        id: 2,
        acciones: { puedeAplicar: false, puedeEmitirNota: false, puedeEditar: false, puedeAnular: true }
      }),
      documentoDe({
        id: 3,
        acciones: { puedeAplicar: false, puedeEmitirNota: false, puedeEditar: false, puedeAnular: false }
      })
    ]);

    const botones = (fila: number) =>
      Array.from(fixture.nativeElement.querySelectorAll('tbody tr')[fila].querySelectorAll('button'))
        .map((b) => (b as HTMLElement).textContent!.trim());
    expect(botones(0)).toEqual(['Cobrar', 'Nota', 'Editar', 'Anular']);
    expect(botones(1)).toEqual(['Anular']);
    expect(botones(2)).toEqual([]);
  });

  it('al editar carga el documento y guarda con PUT, sin tipo de cuenta', () => {
    const { interno, http, fixture, texto } = crearPantalla('por_cobrar');

    interno.editar(documentoDe({ id: 42, descripcion: 'Venta original' }));
    expect(interno.editandoId()).toBe(42);
    expect(interno.form.getRawValue()).toMatchObject({
      idTercero: TERCERO.id,
      tipoDocumento: 'factura',
      numeroDocumento: '1045',
      montoTotal: 119000,
      descripcion: 'Venta original'
    });

    // La rebaja parcial (devolución, descuento posterior) se hace bajando el monto total.
    interno.form.patchValue({ montoTotal: 59500 });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/documentos/42');
    expect(peticion.request.body.tipoCuenta).toBeUndefined();
    expect(peticion.request.body.montoTotal).toBe(59500);
    peticion.flush(documentoDe({ id: 42, montoTotal: 59500 }));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos')
      .flush(pagina([documentoDe({ id: 42, montoTotal: 59500 })]));

    expect(interno.editandoId()).toBeNull();
    fixture.detectChanges();
    expect(texto()).toContain('Cambios guardados.');
  });

  it('pide confirmación antes de anular y solo entonces llama al backend', () => {
    const { interno, http, fixture, pantalla, texto } = crearPantalla('por_pagar');

    interno.pedirAnulacion(documentoDe({ id: 42, tipoCuenta: 'por_pagar' }));
    fixture.detectChanges();
    expect(texto()).toContain(pantalla.preguntaAnular);
    http.expectNone((r) => r.url.includes('/anular'));

    interno.confirmarAnulacion();
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos/42/anular')
      .flush(documentoDe({ id: 42, estadoDocumento: 'anulado', etiquetaEstado: 'Anulada' }));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos')
      .flush(pagina([documentoDe({ id: 42, estadoDocumento: 'anulado', etiquetaEstado: 'Anulada' })]));

    fixture.detectChanges();
    expect(texto()).toContain(pantalla.mensajeAnulada);
    expect(texto()).toContain('Anulada');
  });

  it('muestra el motivo cuando el backend rechaza la anulación por tener pagos', () => {
    const { interno, http, fixture, texto } = crearPantalla('por_cobrar');

    interno.pedirAnulacion(documentoDe({ id: 42 }));
    interno.confirmarAnulacion();
    http.expectOne((r) => r.url === '/api/documentos/42/anular').flush(
      { message: 'Este documento tiene 1 pago registrado. Anula primero ese movimiento de caja y después el documento.' },
      { status: 409, statusText: 'Conflict' }
    );

    fixture.detectChanges();
    expect(texto()).toContain('Anula primero ese movimiento de caja');
  });

  it('sale de edición si se anula justo el documento que se estaba editando', () => {
    const { interno, http } = crearPantalla('por_cobrar');
    const documento = documentoDe({ id: 42 });

    interno.editar(documento);
    interno.pedirAnulacion(documento);
    interno.confirmarAnulacion();
    http.expectOne((r) => r.url === '/api/documentos/42/anular')
      .flush(documentoDe({ id: 42, estadoDocumento: 'anulado' }));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(pagina([]));

    expect(interno.editandoId()).toBeNull();
    expect(interno.form.getRawValue().montoTotal).toBeNull();
  });

  it('muestra la etiqueta de estado tal como la manda el backend', () => {
    const { texto } = crearPantalla('por_cobrar', [TERCERO], [
      documentoDe({ id: 42, estadoDocumento: 'pagado', montoPagado: 0, etiquetaEstado: 'Cerrada por nota' })
    ]);

    expect(texto()).toContain('Cerrada por nota');
  });
});

describe('Documentos (notas de crédito y débito)', () => {
  it('al emitir una nota desde una fila queda elegido el documento y se piden los corregibles', () => {
    const { interno, http } = crearPantalla('por_pagar');

    interno.emitirNota(documentoDe({ id: 42, tipoCuenta: 'por_pagar' }));

    const corregibles = http.expectOne((r) => r.url === '/api/documentos/corregibles');
    expect(corregibles.request.params.get('tipoCuenta')).toBe('por_pagar');
    corregibles.flush([
      { id: 42, tipoDocumento: 'factura', numeroDocumento: '1045', idTercero: 7, nombreTercero: 'X', saldoPendiente: 119000 }
    ]);

    expect(interno.form.getRawValue()).toMatchObject({ tipoDocumento: 'nota_credito', idDocumentoReferencia: 42 });
    expect(interno.form.valid).toBe(false); // falta el monto
    expect(interno.form.hasError('referenciaRequerida')).toBe(false);
    expect(interno.form.hasError('terceroRequerido')).toBe(false);
  });

  it('exige el documento que corrige y no manda la nota sin él', () => {
    const { interno, http } = crearPantalla('por_cobrar');

    interno.form.patchValue({ tipoDocumento: 'nota_credito', montoTotal: 40000, idDocumentoReferencia: null });
    http.expectOne((r) => r.url === '/api/documentos/corregibles').flush([]);
    interno.submit();

    expect(interno.form.hasError('referenciaRequerida')).toBe(true);
    http.expectNone((r) => r.method === 'POST');
  });

  it('manda la nota sin tercero ni vencimiento: los pone el backend', () => {
    const { interno, http, fixture, texto } = crearPantalla('por_cobrar');

    interno.emitirNota(documentoDe({ id: 42 }));
    http.expectOne((r) => r.url === '/api/documentos/corregibles').flush([]);
    interno.form.patchValue({ montoTotal: 40000, numeroDocumento: 'NC-1' });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos');
    expect(peticion.request.body).toMatchObject({
      tipoCuenta: 'por_cobrar',
      tipoDocumento: 'nota_credito',
      idDocumentoReferencia: 42,
      montoTotal: 40000,
      fechaEmision: enDias(0),
      idTercero: null,
      fechaVencimiento: null
    });
    peticion.flush(documentoDe({
      id: 50, tipoDocumento: 'nota_credito', montoTotal: 40000, saldoPendiente: 0,
      idDocumentoReferencia: 42, etiquetaEstado: 'Vigente'
    }));

    // El documento que corrige vuelve con el saldo ya rebajado.
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(
      pagina([documentoDe({ id: 42, montoNotasCredito: 40000, saldoPendiente: 79000 })])
    );

    fixture.detectChanges();
    expect(texto()).toContain('Nota emitida.');
    // El separador de miles depende del locale del entorno, así que se aceptan los dos.
    expect(texto()).toMatch(/79[.,]000/);
    expect(texto()).toMatch(/−40[.,]000/);
  });

  it('un documento normal no manda referencia aunque haya quedado una elegida', () => {
    const { interno, http } = crearPantalla('por_cobrar');

    interno.emitirNota(documentoDe({ id: 42 }));
    http.expectOne((r) => r.url === '/api/documentos/corregibles').flush([]);
    interno.form.patchValue({ tipoDocumento: 'factura', idTercero: TERCERO.id, montoTotal: 10000 });
    interno.submit();

    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/documentos');
    expect(peticion.request.body.idDocumentoReferencia).toBeNull();
    expect(peticion.request.body.idTercero).toBe(TERCERO.id);
    peticion.flush(documentoDe({ id: 60 }));
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/documentos').flush(pagina([]));
  });

  it('muestra el motivo cuando el pago se pasa del saldo', () => {
    const { interno, http, fixture, texto } = crearPantalla('por_cobrar');

    interno.abrirAplicar(documentoDe({ id: 42, saldoPendiente: 84000 }));
    http.expectOne((r) => r.url === '/api/pagos/movimientos-aplicables').flush([
      {
        id: 34, fecha: enDias(0), monto: 100000, montoDisponible: 100000,
        medioPago: 'transferencia', nombreCuenta: 'Caja', descripcion: null
      }
    ]);
    http.expectOne((r) => r.url === '/api/pagos/documento/42').flush([]);

    interno.formAplicar.patchValue({ idMovimiento: 34, montoAplicado: 100000 });
    interno.registrarAplicacion();
    http.expectOne((r) => r.url === '/api/pagos').flush(
      { message: 'El pago no puede pasar de $84.000, que es el saldo que queda del documento.' },
      { status: 409, statusText: 'Conflict' }
    );

    fixture.detectChanges();
    expect(texto()).toContain('El pago no puede pasar de $84.000');
  });
});
