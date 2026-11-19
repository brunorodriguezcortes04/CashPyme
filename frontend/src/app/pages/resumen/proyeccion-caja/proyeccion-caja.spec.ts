import { registerLocaleData } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import localeEsCL from '@angular/common/locales/es-CL';
import { LOCALE_ID } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DiaProyeccion, HorizonteProyeccion, Proyeccion } from '../../../core/proyeccion.service';
import { ProyeccionCaja } from './proyeccion-caja';

/**
 * La proyección la calcula y la guarda el backend (ProyeccionGuardadaTests y
 * ProyeccionYAlertaTests). Acá se prueba que la tarjeta muestre lo guardado sin recalcular, que
 * deje cambiar de horizonte y generar una nueva.
 *
 * El separador de miles depende del locale del entorno (coma en las pruebas, punto en la app),
 * así que las aserciones de montos aceptan los dos. Las fechas sí se prueban en español: se usa
 * el mismo locale que app.config.ts.
 */
registerLocaleData(localeEsCL);

describe('ProyeccionCaja', () => {
  let fixture: ComponentFixture<ProyeccionCaja>;
  let http: HttpTestingController;

  /** Días desde el 23-10-2026 con los saldos dados (sin movimientos salvo los que se indiquen). */
  function dias(saldos: number[], movimientos: Record<number, Partial<DiaProyeccion>> = {}): DiaProyeccion[] {
    return saldos.map((saldo, i) => {
      const fecha = new Date(Date.UTC(2026, 9, 23 + i)).toISOString().slice(0, 10);
      return { fecha, ingresos: 0, egresos: 0, saldo, ...movimientos[i] };
    });
  }

  function horizonteDe(horizonteDias: number, sobrescribe: Partial<HorizonteProyeccion> = {}): HorizonteProyeccion {
    return {
      horizonteDias,
      desde: '2026-10-23',
      hasta: horizonteDias === 30 ? '2026-11-22' : '2026-12-22',
      saldoInicial: 500000,
      saldoFinal: horizonteDias === 30 ? -100000 : 300000,
      totalIngresos: horizonteDias === 30 ? 300000 : 700000,
      totalEgresos: 900000,
      saldoMinimo: -100000,
      fechaSaldoMinimo: '2026-11-12',
      primerDiaNegativo: '2026-11-12',
      dias: dias([500000, 800000, -100000], { 1: { ingresos: 300000 }, 2: { egresos: 900000 } }),
      ...sobrescribe
    };
  }

  function proyeccionDe(sobrescribe: Partial<Proyeccion> = {}): Proyeccion {
    return {
      fechaGeneracion: '2026-10-23T15:05:00Z',
      modeloUsado: 'documentos_recurrentes',
      etiquetaModelo: 'Cuentas por cobrar y por pagar + flujos recurrentes',
      horizontes: [horizonteDe(30), horizonteDe(60)],
      ...sobrescribe
    };
  }

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  function boton(textoBoton: string): HTMLButtonElement {
    const botones = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const encontrado = botones.find((b) => b.textContent?.includes(textoBoton));
    if (!encontrado) {
      throw new Error(`No hay un botón "${textoBoton}"`);
    }
    return encontrado;
  }

  function responderUltima(proyeccion: Proyeccion | null): void {
    const peticion = http.expectOne((r) => r.method === 'GET' && r.url === '/api/proyeccion/ultima');
    if (proyeccion) {
      peticion.flush(proyeccion);
    } else {
      peticion.flush(null, { status: 204, statusText: 'No Content' });
    }
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProyeccionCaja],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: LOCALE_ID, useValue: 'es-CL' }]
    }).compileComponents();

    fixture = TestBed.createComponent(ProyeccionCaja);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  // Criterio: los resultados quedan disponibles con su fecha de generación y el modelo usado.
  it('muestra la última proyección con su fecha de generación y su modelo', () => {
    responderUltima(proyeccionDe());
    const contenido = texto();

    expect(contenido).toContain('Saldo proyectado al 22 de noviembre');
    expect(contenido).toMatch(/\$-100[.,]000/);
    expect(contenido).toMatch(/Hoy parte en \$500[.,]000/);
    expect(contenido).toContain('Generada el 23 de octubre');
    expect(contenido).toContain('Modelo: Cuentas por cobrar y por pagar + flujos recurrentes');
  });

  it('avisa el primer día negativo y pinta el saldo en rojo', () => {
    responderUltima(proyeccionDe());
    const contenido = texto();

    expect(contenido).toContain('Tu caja quedaría en negativo el jueves 12 de noviembre');
    expect(fixture.nativeElement.querySelector('.monto-principal.negativo')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.cero.cruzado')).not.toBeNull();
  });

  it('sin días negativos no avisa', () => {
    responderUltima(
      proyeccionDe({
        horizontes: [horizonteDe(30, { saldoFinal: 600000, saldoMinimo: 500000, primerDiaNegativo: null, dias: dias([500000, 800000, 600000]) })]
      })
    );
    texto();

    expect(fixture.nativeElement.querySelector('.aviso-negativo')).toBeNull();
    expect(fixture.nativeElement.querySelector('.monto-principal.negativo')).toBeNull();
    expect(fixture.nativeElement.querySelector('.cero.cruzado')).toBeNull();
    // Con un solo horizonte no hay pestañas.
    expect(fixture.nativeElement.querySelector('.selector-horizonte')).toBeNull();
  });

  it('cambia entre 30 y 60 días sin pedir nada al backend', () => {
    responderUltima(proyeccionDe());
    texto();

    boton('60 días').click();
    const contenido = texto();

    expect(contenido).toContain('Saldo proyectado al 22 de diciembre');
    expect(contenido).toMatch(/\$300[.,]000/);
    expect(boton('60 días').getAttribute('aria-pressed')).toBe('true');
    expect(boton('30 días').getAttribute('aria-pressed')).toBe('false');
    http.expectNone(() => true);
  });

  it('el detalle lista solo los días en que algo entra o sale', () => {
    responderUltima(proyeccionDe());
    texto();

    const filas = Array.from(fixture.nativeElement.querySelectorAll('.detalle tbody tr')) as HTMLElement[];
    expect(filas.map((f) => f.querySelector('td')?.textContent?.trim())).toEqual(['24/10/2026', '25/10/2026']);
    expect(filas[1].querySelector('.negativo')).not.toBeNull();
  });

  it('sin proyección ofrece generarla', () => {
    responderUltima(null);
    expect(texto()).toContain('Todavía no hay una proyección');

    boton('Generar proyección').click();
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/proyeccion').flush(proyeccionDe());

    expect(texto()).toContain('Saldo proyectado al 22 de noviembre');
  });

  it('al actualizar muestra la nueva y avisa al Resumen', () => {
    responderUltima(proyeccionDe());
    texto();
    const avisos: unknown[] = [];
    fixture.componentInstance.generada.subscribe(() => avisos.push(true));

    boton('Actualizar proyección').click();
    expect(texto()).toContain('Actualizando…');
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/proyeccion').flush(
      proyeccionDe({ fechaGeneracion: '2026-10-24T13:30:00Z' })
    );

    expect(texto()).toContain('Generada el 24 de octubre');
    expect(avisos.length).toBe(1);
  });

  // Generar pide un permiso de escritura que ver no pide: el botón está, pero el backend puede decir que no.
  it('si el rol no puede generar, deja la anterior y dice a quién pedírselo', () => {
    responderUltima(proyeccionDe());
    texto();
    const avisos: unknown[] = [];
    fixture.componentInstance.generada.subscribe(() => avisos.push(true));

    boton('Actualizar proyección').click();
    http.expectOne((r) => r.url === '/api/proyeccion').flush(
      { message: 'No tienes permiso.' },
      { status: 403, statusText: 'Forbidden' }
    );

    const contenido = texto();
    expect(fixture.nativeElement.querySelector('.form-alert[role="alert"]').textContent).toContain(
      'Tu rol no tiene permiso para generar la proyección. Pídele a quien administra la empresa que la actualice.'
    );
    expect(contenido).toContain('Generada el 23 de octubre');
    expect(avisos.length).toBe(0);
  });

  it('si generar falla por otra cosa, deja la anterior y muestra el error', () => {
    responderUltima(proyeccionDe());
    texto();

    boton('Actualizar proyección').click();
    http.expectOne((r) => r.url === '/api/proyeccion').flush(
      { message: 'Error inesperado.' },
      { status: 500, statusText: 'Server Error' }
    );

    const contenido = texto();
    expect(contenido).toContain('Tuvimos un problema de nuestro lado');
    expect(contenido).toContain('Generada el 23 de octubre');
    expect(boton('Actualizar proyección').disabled).toBe(false);
  });

  it('si no se puede cargar, deja reintentar', () => {
    http.expectOne((r) => r.url === '/api/proyeccion/ultima').flush(
      { message: 'Error inesperado.' },
      { status: 500, statusText: 'Server Error' }
    );
    expect(texto()).toContain('Reintentar');

    boton('Reintentar').click();
    responderUltima(proyeccionDe());
    expect(texto()).toContain('Saldo proyectado al');
  });
});
