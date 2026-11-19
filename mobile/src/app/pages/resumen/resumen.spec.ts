import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideIonicAngular } from '@ionic/angular';
import { Resumen } from './resumen';

/** Muestra lo que devuelven los mismos endpoints que la web, sin recalcularlo. */
describe('Resumen (móvil)', () => {
  let fixture: ComponentFixture<Resumen>;
  let http: HttpTestingController;

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Resumen],
      providers: [provideIonicAngular(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(Resumen);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  async function responder(): Promise<void> {
    http.expectOne('/api/dashboard/caja').flush({
      desde: '2026-10-01', hasta: '2026-10-31', saldoTotal: 445000, ingresos: 500000, egresos: 335000, flujoNeto: 165000,
      cuentas: [{ id: 1, nombreCuenta: 'Caja', saldoActual: 445000 }]
    });
    const pedido = http.expectOne((r) => r.url === '/api/movimientos');
    expect([pedido.request.params.get('pagina'), pedido.request.params.get('tamanoPagina')]).toEqual(['1', '10']);
    // Sin tipo ni fechas: ingresos y egresos del período por defecto, como la web.
    expect(pedido.request.params.has('tipo')).toBe(false);
    pedido.flush({
      items: [
        { id: 2, tipoMovimiento: 'egreso', monto: 35000, fecha: '2026-10-20', nombreCuenta: 'Caja', nombreCategoria: 'Servicios básicos', descripcion: 'Luz y agua', etiquetaEstado: 'Registrado' },
        { id: 1, tipoMovimiento: 'ingreso', monto: 500000, fecha: '2026-10-01', nombreCuenta: 'Caja', nombreCategoria: 'Ventas', descripcion: null, etiquetaEstado: 'Registrado' }
      ],
      totalResultados: 2
    });
    await fixture.whenStable();
  }

  it('muestra el saldo total y los últimos movimientos que manda el backend', async () => {
    await responder();
    const contenido = texto();

    expect(contenido).toMatch(/\$\s?445\.000/);
    expect(contenido).toContain('Suma de tus 1 cuenta activa');
    expect(contenido).toContain('Luz y agua');
    expect(contenido).toContain('20-10-2026');
    // Sin descripción se muestra la categoría.
    expect(contenido).toContain('Ventas');
    expect(contenido).toMatch(/−\$\s?35\.000/);
    expect(contenido).toMatch(/\+\$\s?500\.000/);
  });

  it('si falla muestra el mensaje y permite reintentar', async () => {
    http.expectOne('/api/dashboard/caja').flush({ message: 'Tu rol no tiene permiso.' }, { status: 403, statusText: 'Forbidden' });
    // Con una falla se cancela la otra llamada: no queda un resumen a medias.
    expect(http.expectOne((r) => r.url === '/api/movimientos').cancelled).toBe(true);
    await fixture.whenStable();

    expect(texto()).toContain('Tu rol no tiene permiso.');
    (fixture.nativeElement.querySelector('.aviso ion-button') as HTMLElement).click();
    await responder();
    expect(texto()).not.toContain('Tu rol no tiene permiso.');
  });
});
