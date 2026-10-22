import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CajaActual, Indicadores } from '../../core/dashboard.service';
import { Caja } from './caja';

/**
 * Los números y su interpretación (favorable o no, orden del ranking) vienen del backend: acá se
 * prueba que la pantalla los muestre tal cual, sin recalcularlos.
 */
describe('Caja', () => {
  let fixture: ComponentFixture<Caja>;
  let http: HttpTestingController;

  const caja: CajaActual = {
    desde: '2026-10-01',
    hasta: '2026-10-31',
    saldoTotal: 135000,
    ingresos: 125000,
    egresos: 50000,
    flujoNeto: 75000,
    cuentas: [{ id: 1, nombreCuenta: 'Caja', tipoCuenta: 'efectivo', saldoActual: 135000 }],
    flujoDiario: [{ fecha: '2026-10-01', ingresos: 125000, egresos: 50000 }]
  };

  const indicadores: Indicadores = {
    desde: '2026-10-01',
    hasta: '2026-10-31',
    desdeAnterior: '2026-09-01',
    hastaAnterior: '2026-09-30',
    ingresos: { actual: 125000, anterior: 100000, diferencia: 25000, porcentaje: 25, favorable: true },
    egresos: { actual: 50000, anterior: 40000, diferencia: 10000, porcentaje: 25, favorable: false },
    flujoNeto: { actual: 75000, anterior: 0, diferencia: 75000, porcentaje: null, favorable: null },
    topIngresos: [
      { posicion: 1, idCategoria: 1, nombreCategoria: 'Ventas', monto: 125000, cantidadMovimientos: 1, participacion: 100 }
    ],
    topEgresos: [
      { posicion: 1, idCategoria: 5, nombreCategoria: 'Proveedores', monto: 30000, cantidadMovimientos: 1, participacion: 60 },
      { posicion: 2, idCategoria: 3, nombreCategoria: 'Arriendo', monto: 15000, cantidadMovimientos: 1, participacion: 30 }
    ]
  };

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Caja],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(Caja);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('pide caja e indicadores del mismo período y muestra la variación como la manda el backend', () => {
    const pedidoIndicadores = http.expectOne((r) => r.url === '/api/dashboard/indicadores');
    // Sin mes elegido no manda período: el backend usa el mes en curso de la empresa.
    expect(pedidoIndicadores.request.params.keys()).toEqual([]);
    pedidoIndicadores.flush(indicadores);
    http.expectOne((r) => r.url === '/api/dashboard/caja').flush(caja);

    const contenido = texto();
    expect(contenido).toContain('Sin movimientos el mes anterior');

    const variaciones = Array.from(fixture.nativeElement.querySelectorAll('.variacion-pct')) as HTMLElement[];
    expect(variaciones[0].classList).toContain('favorable');
    expect(variaciones[1].classList).toContain('desfavorable');
    expect(variaciones[0].textContent).toMatch(/\+25[.,]0 %/);
  });

  it('muestra el ranking en el orden que llega', () => {
    http.expectOne((r) => r.url === '/api/dashboard/indicadores').flush(indicadores);
    http.expectOne((r) => r.url === '/api/dashboard/caja').flush(caja);
    texto();

    const egresos = Array.from(fixture.nativeElement.querySelectorAll('.ranking'))[0] as HTMLElement;
    const nombres = Array.from(egresos.querySelectorAll('li')).map((li) => li.textContent);
    expect(nombres[0]).toContain('Proveedores');
    expect(nombres[1]).toContain('Arriendo');
  });

  it('al cambiar de mes pide los indicadores de ese mes', () => {
    http.expectOne((r) => r.url === '/api/dashboard/indicadores').flush(indicadores);
    http.expectOne((r) => r.url === '/api/dashboard/caja').flush(caja);
    texto();

    (fixture.nativeElement.querySelector('[aria-label="Mes anterior"]') as HTMLButtonElement).click();

    const pedido = http.expectOne((r) => r.url === '/api/dashboard/indicadores');
    expect([pedido.request.params.get('anio'), pedido.request.params.get('mes')]).toEqual(['2026', '9']);
    pedido.flush({ ...indicadores, desde: '2026-09-01' });
    http.expectOne((r) => r.url === '/api/dashboard/caja').flush({ ...caja, desde: '2026-09-01', hasta: '2026-09-30' });
  });
});
