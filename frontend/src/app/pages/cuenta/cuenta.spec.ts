import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth.service';
import { Cuenta } from './cuenta';

/**
 * Cambiar la contraseña cierra todas las sesiones anteriores en el backend, que responde con
 * una sesión nueva (auth.service.spec.ts prueba dónde se guarda). Acá se prueba que quien la
 * cambia siga dentro y que la pantalla le diga qué pasó con sus otros dispositivos.
 */
describe('Cuenta', () => {
  let fixture: ComponentFixture<Cuenta>;
  let http: HttpTestingController;
  let authService: AuthService;

  function respuestaDe(token: string) {
    return {
      token,
      expiresAtUtc: '2026-11-16T12:00:00Z',
      businessName: 'Mi Negocio SpA',
      email: 'ana@negocio.cl',
      rol: 'Contador'
    };
  }

  function texto(): string {
    fixture.detectChanges();
    return fixture.nativeElement.textContent.replace(/\s+/g, ' ');
  }

  function escribir(id: string, valor: string): void {
    const input = fixture.nativeElement.querySelector(`#${id}`) as HTMLInputElement;
    input.value = valor;
    input.dispatchEvent(new Event('input'));
  }

  /** Llena el formulario, lo envía y devuelve la petición que quedó esperando. */
  function cambiarPassword() {
    escribir('passwordActual', 'clave-vieja');
    escribir('passwordNueva', 'clave-nueva');
    escribir('confirmPassword', 'clave-nueva');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    return http.expectOne((r) => r.method === 'PUT' && r.url === '/api/auth/password');
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [Cuenta],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);

    // Sesión abierta, como la deja el login.
    authService.login({ email: 'ana@negocio.cl', password: 'clave-vieja', rememberMe: false }).subscribe();
    http.expectOne('/api/auth/login').flush(respuestaDe('token-viejo'));

    fixture = TestBed.createComponent(Cuenta);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('al cambiar la contraseña sigue dentro con la sesión nueva y avisa de los otros dispositivos', () => {
    cambiarPassword().flush(respuestaDe('token-nuevo'));
    const contenido = texto();

    const aviso = fixture.nativeElement.querySelector('.form-success[role="status"]') as HTMLElement;
    expect(aviso.textContent).toContain('Tu contraseña se cambió correctamente. Cerramos tu sesión en los demás dispositivos.');
    expect(authService.isAuthenticated()).toBe(true);
    expect(authService.getToken()).toBe('token-nuevo');
    // Los datos de la sesión siguen a la vista: no se cerró ni un instante.
    expect(contenido).toContain('ana@negocio.cl');
    expect((fixture.nativeElement.querySelector('#passwordActual') as HTMLInputElement).value).toBe('');
  });

  it('si la contraseña actual no es correcta muestra el mensaje del backend y no toca la sesión', () => {
    cambiarPassword().flush(
      { message: 'La contraseña actual no es correcta.' },
      { status: 400, statusText: 'Bad Request' }
    );
    texto();

    const alerta = fixture.nativeElement.querySelector('.form-alert[role="alert"]') as HTMLElement;
    expect(alerta.textContent).toContain('La contraseña actual no es correcta.');
    expect(fixture.nativeElement.querySelector('.form-success')).toBeNull();
    expect(authService.getToken()).toBe('token-viejo');
  });
});
