import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { Router, provideRouter } from '@angular/router';
import { provideIonicAngular } from '@ionic/angular';
import { Login } from './login';

type Interno = { form: FormGroup; entrar(): Promise<void> };

describe('Login (móvil)', () => {
  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;
  let interno: Interno;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideIonicAngular(), provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
    interno = fixture.componentInstance as unknown as Interno;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('entra con el mismo endpoint que la web y va al resumen', async () => {
    const navegar = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    interno.form.setValue({ email: 'ana@pyme.cl', password: 'Clave12345' });

    const envio = interno.entrar();
    const pedido = http.expectOne('/api/auth/login');
    expect(pedido.request.body).toEqual({ email: 'ana@pyme.cl', password: 'Clave12345', rememberMe: true });
    pedido.flush({ token: 'jwt', expiresAtUtc: '', businessName: 'Panadería', email: 'ana@pyme.cl', rol: 'Administrador' });
    await envio;

    expect(navegar).toHaveBeenCalledWith('/resumen', { replaceUrl: true });
    expect(localStorage.getItem('CapacitorStorage.cashpyme.sesion')).toContain('"token":"jwt"');
  });

  it('muestra el mensaje del backend si las credenciales no sirven', async () => {
    interno.form.setValue({ email: 'ana@pyme.cl', password: 'mala' });

    const envio = interno.entrar();
    http.expectOne('/api/auth/login').flush(
      { message: 'Correo o contraseña incorrectos.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    await envio;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Correo o contraseña incorrectos.');
  });

  it('sin datos no llama al backend', async () => {
    await interno.entrar();
    http.expectNone('/api/auth/login');
  });
});
