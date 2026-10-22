import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Movimientos } from './movimientos';

type Interno = { filtrosForm: FormGroup; aplicarFiltros(): void; exportar(formato: 'csv' | 'xlsx'): void };

/** La exportación baja lo que se está viendo: los filtros del último listado, no lo escrito sin aplicar. */
describe('Movimientos (exportar)', () => {
  let fixture: ComponentFixture<Movimientos>;
  let http: HttpTestingController;
  let interno: Interno;

  function pagina(desde = '2026-10-01', hasta = '2026-10-31') {
    return { items: [], totalResultados: 0, pagina: 1, tamanoPagina: 20, fechaDesde: desde, fechaHasta: hasta, periodoPorDefecto: true };
  }

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Movimientos],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ tipo: 'egreso' })) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(Movimientos);
    interno = fixture.componentInstance as unknown as Interno;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne((r) => r.url === '/api/opciones/tipos-movimiento').flush([]);
    http.expectOne((r) => r.url === '/api/opciones/medios-pago').flush([]);
    http.expectOne((r) => r.url === '/api/cuentas').flush([]);
    http.expectOne((r) => r.url === '/api/categorias').flush([]);
    http.expectOne((r) => r.url === '/api/movimientos').flush(pagina());
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  function pedidoExport(): TestRequest {
    return http.expectOne((r) => r.url === '/api/movimientos/export');
  }

  it('exporta con el tipo y el rango que aplicó el backend, sin paginar', () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    interno.exportar('xlsx');

    const pedido = pedidoExport();
    const p = pedido.request.params;
    expect([p.get('formato'), p.get('tipo'), p.get('fechaDesde'), p.get('fechaHasta')])
      .toEqual(['xlsx', 'egreso', '2026-10-01', '2026-10-31']);
    expect(p.has('pagina')).toBe(false);
    pedido.flush(new Blob(['x']), {
      headers: { 'Content-Disposition': 'attachment; filename=movimientos-egreso-2026-10-01-a-2026-10-31.xlsx' }
    });

    expect((click.mock.instances[0] as unknown as HTMLAnchorElement).download)
      .toBe('movimientos-egreso-2026-10-01-a-2026-10-31.xlsx');
  });

  it('usa los filtros aplicados, no lo escrito en el formulario sin filtrar', () => {
    interno.filtrosForm.patchValue({ idCuenta: '9' });
    interno.exportar('csv');

    const pedido = pedidoExport();
    expect(pedido.request.params.has('idCuenta')).toBe(false);
    pedido.flush(new Blob(['x']));
  });

  it('después de filtrar exporta con el filtro nuevo', () => {
    interno.filtrosForm.patchValue({ idCuenta: '9', fechaDesde: '2026-09-01', fechaHasta: '2026-09-30' });
    interno.aplicarFiltros();
    http.expectOne((r) => r.url === '/api/movimientos').flush(pagina('2026-09-01', '2026-09-30'));

    interno.exportar('csv');

    const pedido = pedidoExport();
    const p = pedido.request.params;
    expect([p.get('idCuenta'), p.get('fechaDesde'), p.get('fechaHasta')]).toEqual(['9', '2026-09-01', '2026-09-30']);
    pedido.flush(new Blob(['x']));
  });

  it('muestra el mensaje del backend si la exportación falla', async () => {
    interno.exportar('csv');

    pedidoExport().flush(new Blob([JSON.stringify({ message: 'El formato de exportación debe ser csv o xlsx.' })]), {
      status: 400,
      statusText: 'Bad Request'
    });
    await fixture.whenStable();
    await new Promise((resolver) => setTimeout(resolver));

    expect(texto()).toContain('El formato de exportación debe ser csv o xlsx.');
  });
});
