import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { mensajeDeError } from '../../core/api-error';
import { EmpresaService, Rol, UsuarioEmpresa } from '../../core/empresa.service';

@Component({
  selector: 'app-usuarios',
  imports: [ReactiveFormsModule],
  templateUrl: './usuarios.html',
  styleUrl: './usuarios.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Usuarios {
  private readonly formBuilder = inject(FormBuilder);
  private readonly empresaService = inject(EmpresaService);

  protected readonly usuarios = signal<UsuarioEmpresa[]>([]);
  protected readonly roles = signal<Rol[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  /** Usuario cuya fila está en medio de un cambio de rol o estado: desactiva sus controles. */
  protected readonly actualizandoId = signal<number | null>(null);

  /**
   * Contraseña provisional recién generada. Se muestra en un aviso destacado hasta que la
   * persona lo cierra, porque el backend no la vuelve a entregar nunca más.
   */
  protected readonly credencialNueva = signal<{ email: string; password: string } | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    nombre: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(150)]],
    idRol: [null as number | null, [Validators.required]]
  });

  constructor() {
    this.cargar();
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { nombre, email, idRol } = this.form.getRawValue();

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.credencialNueva.set(null);

    this.empresaService.agregarUsuario({ nombre, email, idRol: idRol! }).subscribe({
      next: ({ usuario, passwordProvisional }) => {
        this.usuarios.update((actuales) => [...actuales, usuario]);

        if (passwordProvisional) {
          this.credencialNueva.set({ email: usuario.email, password: passwordProvisional });
        } else {
          // Sin contraseña provisional la persona ya tenía cuenta: conserva la suya.
          this.successMessage.set(
            `${usuario.nombre} ya tenía cuenta en CashPyme; ahora también tiene acceso a esta empresa con su contraseña de siempre.`
          );
        }

        this.form.reset({ nombre: '', email: '', idRol: null });
        this.isSubmitting.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isSubmitting.set(false);
      }
    });
  }

  protected cambiarRol(usuario: UsuarioEmpresa, event: Event): void {
    const idRol = Number((event.target as HTMLSelectElement).value);
    this.ejecutar(usuario.idUsuario, this.empresaService.cambiarRol(usuario.idUsuario, idRol));
  }

  protected cambiarEstado(usuario: UsuarioEmpresa): void {
    this.ejecutar(
      usuario.idUsuario,
      this.empresaService.cambiarEstado(usuario.idUsuario, !usuario.activo)
    );
  }

  protected cerrarCredencial(): void {
    this.credencialNueva.set(null);
  }

  protected idRolDe(usuario: UsuarioEmpresa): number | null {
    return this.roles().find((r) => r.nombreRol === usuario.rol)?.id ?? null;
  }

  /**
   * Cambios de rol y de estado comparten manejo: si el backend rechaza (por ejemplo, al
   * intentar dejar la empresa sin Administrador) se recarga la lista para que la fila
   * vuelva a mostrar el valor real y no el que alcanzó a elegir la persona.
   */
  private ejecutar(idUsuario: number, peticion: ReturnType<EmpresaService['cambiarEstado']>): void {
    this.actualizandoId.set(idUsuario);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    peticion.subscribe({
      next: (actualizado) => {
        this.usuarios.update((actuales) =>
          actuales.map((u) => (u.idUsuario === actualizado.idUsuario ? actualizado : u))
        );
        this.actualizandoId.set(null);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.actualizandoId.set(null);
        this.cargarUsuarios();
      }
    });
  }

  private cargar(): void {
    this.empresaService.listRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (error: unknown) => this.errorMessage.set(mensajeDeError(error))
    });

    this.cargarUsuarios();
  }

  private cargarUsuarios(): void {
    this.isLoading.set(true);

    this.empresaService.listUsuarios().subscribe({
      next: (usuarios) => {
        this.usuarios.set(usuarios);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(mensajeDeError(error));
        this.isLoading.set(false);
      }
    });
  }
}
