import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Login } from './login';

/**
 * Quién puede entrar lo decide el backend, y también cuándo frena los intentos (429 por límite
 * de intentos o cuenta bloqueada un rato). Acá se prueba que la pantalla muestre lo que el
 * backend responde, en una alerta que el lector de pantalla anuncia.
 */
describe('Login', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  function elemento(): HTMLElement {
    harness.detectChanges();
    return harness.routeNativeElement as HTMLElement;
  }

  function texto(): string {
    return elemento().textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function escribir(id: string, valor: string): void {
    const input = elemento().querySelector(`#${id}`) as HTMLInputElement;
    input.value = valor;
    input.dispatchEvent(new Event('input'));
  }

  /** Llena el formulario, lo envía y devuelve la petición de login que quedó esperando. */
  function intentarEntrar() {
    escribir('email', 'ana@negocio.cl');
    escribir('password', 'clave-segura');
    elemento().querySelector('form')?.dispatchEvent(new Event('submit'));
    return http.expectOne((r) => r.method === 'POST' && r.url === '/api/auth/login');
  }

  function alerta(): string | undefined {
    return elemento().querySelector('.form-alert[role="alert"]')?.textContent?.trim();
  }

  async function abrir(url = '/login'): Promise<void> {
    harness = await RouterTestingHarness.create(url);
    http = TestBed.inject(HttpTestingController);
  }

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'login', component: Login }])]
    });
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('con demasiados intentos muestra el mensaje del backend y deja volver a intentar', async () => {
    await abrir();

    intentarEntrar().flush(
      { message: 'Tu cuenta está bloqueada por demasiados intentos fallidos. Inténtalo de nuevo en 15 minutos.' },
      { status: 429, statusText: 'Too Many Requests' }
    );

    expect(alerta()).toBe('Tu cuenta está bloqueada por demasiados intentos fallidos. Inténtalo de nuevo en 15 minutos.');
    const enviar = elemento().querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(enviar.disabled).toBe(false);
    expect(enviar.textContent).toContain('Iniciar sesión');
  });

  it('un 429 sin mensaje no manda a revisar los datos', async () => {
    await abrir();

    intentarEntrar().flush(null, { status: 429, statusText: 'Too Many Requests' });

    expect(alerta()).toBe('Hiciste demasiados intentos. Espera unos minutos e inténtalo de nuevo.');
  });

  it('con credenciales incorrectas muestra el mensaje del backend', async () => {
    await abrir();

    intentarEntrar().flush({ message: 'Correo o contraseña incorrectos.' }, { status: 401, statusText: 'Unauthorized' });

    expect(alerta()).toBe('Correo o contraseña incorrectos.');
  });

  it('si llega porque el backend cerró su sesión, explica por qué', async () => {
    await abrir('/login?sesionExpirada=1');

    const aviso = elemento().querySelector('.form-alert[role="status"]');
    expect(aviso?.textContent?.replace(/\s+/g, ' ')).toContain(
      'Cerramos tu sesión por seguridad: venció o tu contraseña se cambió en otro dispositivo.'
    );
  });

  it('sin ese aviso en la URL el login abre limpio', async () => {
    await abrir();

    expect(texto()).not.toContain('Cerramos tu sesión');
    expect(elemento().querySelector('.form-alert')).toBeNull();
  });
});
