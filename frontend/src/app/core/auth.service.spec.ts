import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

/**
 * Al cambiar la contraseña el backend invalida todos los tokens anteriores, también el de esta
 * sesión, y devuelve uno nuevo. Acá se prueba que el front se quede con ese, en el mismo
 * almacenamiento donde ya estaba la sesión, para que la persona no tenga que volver a entrar.
 */
describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  function respuestaDe(token: string) {
    return {
      token,
      expiresAtUtc: '2026-11-16T12:00:00Z',
      businessName: 'Mi Negocio SpA',
      email: 'ana@negocio.cl',
      rol: 'Administrador'
    };
  }

  /** Deja la sesión como la deja el login, con o sin "Mantener sesión iniciada". */
  function iniciarSesion(rememberMe: boolean): void {
    service.login({ email: 'ana@negocio.cl', password: 'clave-vieja', rememberMe }).subscribe();
    http.expectOne('/api/auth/login').flush(respuestaDe('token-viejo'));
  }

  function cambiarPassword() {
    service.cambiarPassword('clave-vieja', 'clave-nueva').subscribe({ error: () => undefined });
    return http.expectOne((r) => r.method === 'PUT' && r.url === '/api/auth/password');
  }

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('al cambiar la contraseña manda la actual y la nueva, y se queda con el token que vuelve', () => {
    iniciarSesion(false);

    const peticion = cambiarPassword();
    expect(peticion.request.body).toEqual({ passwordActual: 'clave-vieja', passwordNueva: 'clave-nueva' });
    peticion.flush(respuestaDe('token-nuevo'));

    expect(service.getToken()).toBe('token-nuevo');
    expect(service.isAuthenticated()).toBe(true);
    expect(service.session()).toEqual({ businessName: 'Mi Negocio SpA', email: 'ana@negocio.cl', rol: 'Administrador' });
  });

  it('una sesión recordada sigue recordada con el token nuevo', () => {
    iniciarSesion(true);

    cambiarPassword().flush(respuestaDe('token-nuevo'));

    expect(localStorage.getItem('cashpyme.token')).toBe('token-nuevo');
    expect(sessionStorage.getItem('cashpyme.token')).toBeNull();
  });

  it('una sesión sin recordar no pasa a quedar guardada en el equipo', () => {
    iniciarSesion(false);

    cambiarPassword().flush(respuestaDe('token-nuevo'));

    expect(sessionStorage.getItem('cashpyme.token')).toBe('token-nuevo');
    expect(localStorage.getItem('cashpyme.token')).toBeNull();
  });

  it('si el cambio falla, la sesión queda como estaba', () => {
    iniciarSesion(false);

    cambiarPassword().flush(
      { message: 'La contraseña actual no es correcta.' },
      { status: 400, statusText: 'Bad Request' }
    );

    expect(service.getToken()).toBe('token-viejo');
    expect(service.isAuthenticated()).toBe(true);
  });
});
