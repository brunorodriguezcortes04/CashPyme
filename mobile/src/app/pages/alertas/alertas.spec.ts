import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideIonicAngular } from '@ionic/angular';
import { Alertas } from './alertas';

/** Lista lo que devuelve el mismo GET /alertas que la web, en el orden del backend. */
describe('Alertas (móvil)', () => {
  let fixture: ComponentFixture<Alertas>;
  let http: HttpTestingController;

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Alertas],
      providers: [provideIonicAngular(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(Alertas);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('lista las alertas pendientes tal como las manda el backend', async () => {
    http.expectOne('/api/alertas').flush([
      { id: 1, tipoAlerta: 'documento_vencido', nivelSeveridad: 'critica', etiquetaSeveridad: 'Urgente', mensaje: 'Factura 120 vencida', fechaReferencia: '2026-10-20', idDocumento: 5, tipoCuenta: 'por_cobrar', estadoAlerta: 'pendiente', fechaGeneracion: '2026-10-23T08:00:00Z', ruta: 'cuentas-por-cobrar' },
      { id: 2, tipoAlerta: 'saldo_bajo', nivelSeveridad: 'info', etiquetaSeveridad: 'Aviso', mensaje: 'Saldo bajo', fechaReferencia: null, idDocumento: null, tipoCuenta: null, estadoAlerta: 'pendiente', fechaGeneracion: '2026-10-23T08:00:00Z', ruta: null }
    ]);
    await fixture.whenStable();
    const contenido = texto();

    expect(contenido).toContain('Urgente');
    expect(contenido).toContain('Factura 120 vencida');
    expect(contenido).toContain('Aviso');
    // No se reordena: las críticas primero, como las manda el backend.
    expect(contenido.indexOf('Factura 120 vencida')).toBeLessThan(contenido.indexOf('Saldo bajo'));
    // El color sale de nivelSeveridad.
    expect(fixture.nativeElement.querySelector('.alerta-critica')).not.toBeNull();
  });

  it('sin alertas muestra el mismo texto que la web', async () => {
    http.expectOne('/api/alertas').flush([]);
    await fixture.whenStable();

    expect(texto()).toContain('No tienes alertas pendientes.');
  });

  it('muestra el mensaje del backend si el rol no puede ver alertas', async () => {
    http.expectOne('/api/alertas').flush({ message: 'Tu rol no tiene permiso.' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();

    const contenido = texto();
    expect(contenido).toContain('Tu rol no tiene permiso.');
    expect(contenido).not.toContain('No tienes alertas pendientes.');
  });
});
