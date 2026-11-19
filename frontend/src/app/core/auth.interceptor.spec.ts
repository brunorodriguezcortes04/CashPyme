import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

/**
 * El backend puede dejar de aceptar un token en cualquier momento: venció, la contraseña cambió
 * en otro dispositivo o desactivaron al usuario. Acá se prueba que ese 401 cierre la sesión y
 * mande al login con el aviso, sin confundirse con el 401 de unas credenciales incorrectas ni
 * con el de un token que esta misma sesión ya reemplazó.
 */
describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let authService: AuthService;
  let navegar: ReturnType<typeof vi.spyOn>;

  function respuestaDe(token: string) {
    return {
      token,
      expiresAtUtc: '2026-11-16T12:00:00Z',
      businessName: 'Mi Negocio SpA',
      email: 'ana@negocio.cl',
      rol: 'Administrador'
    };
  }

  const noAutorizado = { status: 401, statusText: 'Unauthorized' };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
    navegar = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    // Sesión abierta, como la deja el login.
    authService.login({ email: 'ana@negocio.cl', password: 'clave-vieja', rememberMe: false }).subscribe();
    backend.expectOne('/api/auth/login').flush(respuestaDe('token-vigente'));
  });

  afterEach(() => {
    backend.verify();
    vi.restoreAllMocks();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('manda el token de la sesión en las peticiones a la API', () => {
    http.get('/api/alertas').subscribe();

    const peticion = backend.expectOne('/api/alertas');
    expect(peticion.request.headers.get('Authorization')).toBe('Bearer token-vigente');
    peticion.flush([]);
  });

  it('si el backend ya no acepta el token, cierra la sesión y manda al login con el aviso', () => {
    http.get('/api/alertas').subscribe({ error: () => undefined });
    backend.expectOne('/api/alertas').flush(null, noAutorizado);

    expect(authService.isAuthenticated()).toBe(false);
    expect(authService.getToken()).toBeNull();
    expect(navegar).toHaveBeenCalledWith(['/login'], { queryParams: { sesionExpirada: 1 } });
  });

  it('un error que no es de sesión deja la sesión abierta', () => {
    http.post('/api/proyeccion', {}).subscribe({ error: () => undefined });
    backend.expectOne('/api/proyeccion').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(authService.isAuthenticated()).toBe(true);
    expect(navegar).not.toHaveBeenCalled();
  });

  it('el login va sin el token guardado y su 401 no cierra la sesión que ya estaba', () => {
    let mensaje = '';
    authService
      .login({ email: 'otra@negocio.cl', password: 'equivocada', rememberMe: false })
      .subscribe({ error: (error: Error) => (mensaje = error.message) });

    const peticion = backend.expectOne('/api/auth/login');
    expect(peticion.request.headers.has('Authorization')).toBe(false);
    peticion.flush({ message: 'Correo o contraseña incorrectos.' }, noAutorizado);

    expect(mensaje).toBe('Correo o contraseña incorrectos.');
    expect(authService.getToken()).toBe('token-vigente');
    expect(navegar).not.toHaveBeenCalled();
  });

  it('el registro tampoco lleva el token guardado', () => {
    authService
      .register({ businessName: 'Otro Negocio', email: 'otra@negocio.cl', password: 'clave-segura' })
      .subscribe({ error: () => undefined });

    const peticion = backend.expectOne('/api/auth/register');
    expect(peticion.request.headers.has('Authorization')).toBe(false);
    peticion.flush({ message: 'Ya existe una cuenta con este correo electrónico.' }, { status: 409, statusText: 'Conflict' });

    expect(authService.getToken()).toBe('token-vigente');
  });

  // Cambiar la contraseña invalida el token con que salió una petición que aún no vuelve.
  it('el 401 de un token que la sesión ya reemplazó no se lleva la sesión nueva', () => {
    http.get('/api/alertas').subscribe({ error: () => undefined });
    const pendiente = backend.expectOne('/api/alertas');

    authService.cambiarPassword('clave-vieja', 'clave-nueva').subscribe();
    backend.expectOne('/api/auth/password').flush(respuestaDe('token-nuevo'));
    pendiente.flush(null, noAutorizado);

    expect(authService.getToken()).toBe('token-nuevo');
    expect(authService.isAuthenticated()).toBe(true);
    expect(navegar).not.toHaveBeenCalled();
  });
});
