import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Alerta } from '../../core/alertas.service';
import { SaldoNeto } from '../../core/dashboard.service';
import { Resumen } from './resumen';

/**
 * Los cálculos viven en el backend y se prueban en tests/Backend.Tests (SaldoNetoTests,
 * ReglasAlertasTests y SaldoNetoYAlertasTests). Acá se prueba que la pantalla muestre lo que el backend devuelve,
 * sin volver a sumar ni a decidir nada.
 *
 * El separador de miles depende del locale del entorno (coma en las pruebas, punto en la app),
 * así que las aserciones de montos aceptan los dos.
 */
describe('Resumen', () => {
  let fixture: ComponentFixture<Resumen>;
  let http: HttpTestingController;

  function saldoDe(sobrescribe: Partial<SaldoNeto> = {}): SaldoNeto {
    return {
      saldoCaja: 500000,
      porCobrar: 300000,
      porPagar: 200000,
      saldoNeto: 600000,
      documentosPorCobrar: 1,
      documentosPorPagar: 1,
      ...sobrescribe
    };
  }

  function alertaDe(sobrescribe: Partial<Alerta> = {}): Alerta {
    return {
      id: 1,
      tipoAlerta: 'vencimiento_proximo',
      nivelSeveridad: 'advertencia',
      etiquetaSeveridad: 'Atención',
      mensaje: 'Cobro de Distribuidora Los Andes (N° 1045) vence el 16-10-2026 (saldo $119.000)',
      fechaReferencia: '2026-10-16',
      idDocumento: 7,
      tipoCuenta: 'por_cobrar',
      estadoAlerta: 'pendiente',
      fechaGeneracion: '2026-10-11T12:00:00Z',
      ruta: 'cuentas-por-cobrar',
      ...sobrescribe
    };
  }

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Resumen],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(Resumen);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  /** Las dos llamadas que hace la pantalla al abrir: la tarjeta de saldo y las alertas. */
  function responder(saldo: SaldoNeto, alertas: Alerta[] = []): void {
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/dashboard/saldo-neto').flush(saldo);
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/alertas').flush(alertas);
  }

  // ----- Saldo neto proyectado ----------------------------------------------

  // Criterio 1: caja 500.000 + CxC 300.000 - CxP 200.000 = 600.000.
  it('muestra el saldo neto y el desglose que lo explica', () => {
    responder(saldoDe());
    const contenido = texto();

    expect(contenido).toMatch(/\$600[.,]000/);
    expect(contenido).toContain('Saldo en caja');
    expect(contenido).toMatch(/\$500[.,]000/);
    expect(contenido).toMatch(/\$300[.,]000/);
    expect(contenido).toMatch(/\$200[.,]000/);
    expect(contenido).toContain('(1 documento)');
  });

  // Criterio 2: sin pendientes, el neto es el saldo de caja.
  it('sin pendientes muestra el saldo de caja y no el desglose', () => {
    responder(saldoDe({ porCobrar: 0, porPagar: 0, saldoNeto: 500000, documentosPorCobrar: 0, documentosPorPagar: 0 }));
    const contenido = texto();

    expect(contenido).toMatch(/\$500[.,]000/);
    expect(contenido).toContain('es tu saldo en caja');
    expect(fixture.nativeElement.querySelector('.desglose')).toBeNull();
  });

  it('avisa cuando el neto es negativo', () => {
    responder(saldoDe({ saldoCaja: 100000, porCobrar: 50000, porPagar: 400000, saldoNeto: -250000 }));
    const contenido = texto();

    expect(fixture.nativeElement.querySelector('.saldo-neto.negativo')).not.toBeNull();
    expect(contenido).toContain('no alcanza para pagar');
  });

  it('no avisa negativo cuando el neto es positivo', () => {
    responder(saldoDe());
    texto();

    expect(fixture.nativeElement.querySelector('.saldo-neto.negativo')).toBeNull();
    expect(fixture.nativeElement.querySelector('.aviso-negativo')).toBeNull();
  });

  it('aclara que no es una proyección en el tiempo', () => {
    responder(saldoDe());
    expect(texto()).toContain('no es una proyección en el tiempo');
  });

  it('muestra el error del saldo y deja reintentar', () => {
    http.expectOne((r) => r.url === '/api/dashboard/saldo-neto').flush(
      { message: 'No tienes permiso.' },
      { status: 403, statusText: 'Forbidden' }
    );
    http.expectOne((r) => r.url === '/api/alertas').flush([]);
    expect(texto()).toContain('Reintentar');

    (fixture.nativeElement.querySelector('.tarjeta-saldo .form-alert button') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === '/api/dashboard/saldo-neto').flush(saldoDe());
    expect(texto()).toMatch(/\$600[.,]000/);
  });

  // ----- Alertas --------------------------------------------------------------

  it('muestra las alertas pendientes con su mensaje y cuántas son', () => {
    responder(saldoDe(), [
      alertaDe(),
      alertaDe({ id: 2, mensaje: 'Pago de Insumos del Sur vence el 14-10-2026 (saldo $250.000)', tipoCuenta: 'por_pagar' })
    ]);
    const contenido = texto();

    expect(contenido).toContain('Cobro de Distribuidora Los Andes (N° 1045) vence el 16-10-2026');
    expect(contenido).toContain('Pago de Insumos del Sur');
    expect(fixture.nativeElement.querySelector('.contador')?.textContent.trim()).toBe('2');
  });

  it('sin alertas lo dice en vez de dejar la tarjeta vacía', () => {
    responder(saldoDe(), []);
    expect(texto()).toContain('No tienes alertas pendientes.');
    expect(fixture.nativeElement.querySelector('.contador')).toBeNull();
  });

  it('muestra la severidad y el enlace que manda el backend, sin decidirlos', () => {
    responder(saldoDe(), [
      alertaDe({ id: 1, nivelSeveridad: 'critica', etiquetaSeveridad: 'Urgente', tipoAlerta: 'documento_vencido' }),
      alertaDe({ id: 2, tipoCuenta: 'por_pagar', ruta: 'cuentas-por-pagar' }),
      alertaDe({ id: 3, tipoAlerta: 'saldo_bajo', idDocumento: null, tipoCuenta: null, ruta: 'cuentas' }),
      alertaDe({
        id: 4, tipoAlerta: 'saldo_negativo_proyectado', idDocumento: null, tipoCuenta: null,
        nivelSeveridad: 'critica', etiquetaSeveridad: 'Urgente', ruta: null
      })
    ]);
    texto();

    const items = Array.from(fixture.nativeElement.querySelectorAll('.alerta')) as HTMLElement[];
    expect(items[0].classList).toContain('alerta-critica');
    expect(items[0].textContent).toContain('Urgente');
    expect(items[1].classList).toContain('alerta-advertencia');
    expect(items[1].textContent).toContain('Atención');

    const destino = (i: number) => items[i].querySelector('a')?.getAttribute('href') ?? null;
    expect(destino(0)).toBe('/backoffice/cuentas-por-cobrar');
    expect(destino(1)).toBe('/backoffice/cuentas-por-pagar');
    expect(destino(2)).toBe('/backoffice/cuentas');
    // Sin ruta (la proyección no tiene pantalla propia) no se inventa un enlace.
    expect(destino(3)).toBeNull();
  });

  it('marcar como leída la saca de la lista', () => {
    responder(saldoDe(), [alertaDe({ id: 1 }), alertaDe({ id: 2, mensaje: 'Otra alerta' })]);
    texto();

    (fixture.nativeElement.querySelector('.alerta button') as HTMLButtonElement).click();
    const peticion = http.expectOne((r) => r.method === 'POST' && r.url === '/api/alertas/1/leida');
    peticion.flush(null);

    const contenido = texto();
    expect(contenido).not.toContain('Distribuidora Los Andes');
    expect(contenido).toContain('Otra alerta');
    expect(fixture.nativeElement.querySelector('.contador')?.textContent.trim()).toBe('1');
  });

  it('si no se puede marcar como leída, la deja y muestra el error', () => {
    responder(saldoDe(), [alertaDe({ id: 1 })]);
    texto();

    (fixture.nativeElement.querySelector('.alerta button') as HTMLButtonElement).click();
    http.expectOne((r) => r.url === '/api/alertas/1/leida').flush(
      { message: 'La alerta no existe o no pertenece a tu empresa.' },
      { status: 404, statusText: 'Not Found' }
    );

    const contenido = texto();
    expect(contenido).toContain('La alerta no existe');
    expect(contenido).toContain('Distribuidora Los Andes');
  });
});
