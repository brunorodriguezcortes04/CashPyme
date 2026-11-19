import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Register } from './register';

/**
 * El backend frena los registros repetidos con un 429 y su propio mensaje. Acá se prueba que la
 * pantalla lo muestre en una alerta que el lector de pantalla anuncia, igual que el resto de
 * los errores del registro.
 */
describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let http: HttpTestingController;

  function escribir(id: string, valor: string): void {
    const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
    input.value = valor;
    input.dispatchEvent(new Event('input'));
  }

  /** Llena el formulario, lo envía y devuelve la petición de registro que quedó esperando. */
  function intentarRegistro() {
    escribir('businessName', 'Mi Negocio SpA');
    escribir('email', 'ana@negocio.cl');
    escribir('password', 'clave-segura');
    escribir('confirmPassword', 'clave-segura');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    return http.expectOne((r) => r.method === 'POST' && r.url === '/api/auth/register');
  }

  function alerta(): string | undefined {
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('.form-alert[role="alert"]')?.textContent?.trim();
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(Register);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('con demasiados intentos muestra el mensaje del backend y deja volver a intentar', () => {
    intentarRegistro().flush(
      { message: 'Hiciste demasiados intentos. Espera unos minutos e inténtalo de nuevo.' },
      { status: 429, statusText: 'Too Many Requests' }
    );

    expect(alerta()).toBe('Hiciste demasiados intentos. Espera unos minutos e inténtalo de nuevo.');
    const enviar = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(enviar.disabled).toBe(false);
    expect(enviar.textContent).toContain('Crear cuenta');
  });

  it('con un correo ya registrado muestra el mensaje del backend', () => {
    intentarRegistro().flush(
      { message: 'Ya existe una cuenta con este correo electrónico.' },
      { status: 409, statusText: 'Conflict' }
    );

    expect(alerta()).toBe('Ya existe una cuenta con este correo electrónico.');
  });
});
