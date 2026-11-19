using System.Security.Cryptography;
using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Backend.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class AuthService(
    AppDbContext db,
    JwtTokenService jwtTokenService,
    CorreoCuentaService correoCuenta,
    IntentosLoginService intentosLogin,
    TimeProvider reloj)
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    /// <summary>
    /// Usuario de mentira con el hash de una contraseña que nadie conoce. Cuando el correo no
    /// existe se verifica contra este, para que el login tarde lo mismo que con un correo
    /// registrado: si respondiera antes, el tiempo de respuesta diría qué correos tienen cuenta.
    /// </summary>
    private static readonly User UsuarioFicticio = CrearUsuarioFicticio();

    /// <summary>
    /// Registra una pyme: crea empresa + usuario + membresía (rol Administrador)
    /// en UNA sola transacción (un único SaveChanges).
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
        {
            throw new EmailAlreadyRegisteredException();
        }

        var adminRoleId = await db.Roles
            .Where(r => r.Name == Role.Administrator)
            .Select(r => (short?)r.Id)
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"No existe el rol '{Role.Administrator}'. ¿Ejecutaste database/cashpyme_modelo_datos_v2.sql en Supabase?");

        var businessName = request.BusinessName.Trim();

        var company = new Company { LegalName = businessName };
        var user = new User
        {
            // El registro solo pide nombre del negocio; el nombre de la persona se completa en el perfil.
            Name = businessName.Length > 100 ? businessName[..100] : businessName,
            Email = normalizedEmail,
            PasswordHash = string.Empty
        };
        user.PasswordHash = PasswordHasher.HashPassword(user, request.Password);

        db.CompanyMemberships.Add(new CompanyMembership
        {
            Company = company,
            User = user,
            RoleId = adminRoleId
        });

        await db.EnTransaccionAsync(async () =>
        {
            // Dos registros simultáneos con el mismo correo (ux_usuario_email).
            await db.SaveChangesTraducidoAsync(() => new EmailAlreadyRegisteredException());

            // Lo que toda empresa nueva trae para poder cargar movimientos desde el primer minuto.
            // Antes lo creaban los triggers de la tabla empresa; va en la misma transacción para
            // que no quede nunca una empresa a medio crear, sin cuenta o sin categorías.
            db.CategoriasMovimiento.AddRange(DatosBaseEmpresa.Categorias.Select(c => new CategoriaMovimiento
            {
                CompanyId = company.Id,
                Name = c.Nombre,
                Type = c.Tipo
            }));
            db.CuentasFinancieras.Add(new CuentaFinanciera
            {
                CompanyId = company.Id,
                Name = DatosBaseEmpresa.NombreCuentaBase,
                Type = DatosBaseEmpresa.TipoCuentaBase,
                InitialBalance = 0
            });
            await db.SaveChangesAuditadoAsync(user.Id);
        });

        // Después del commit: si el correo falla, la cuenta igual existe (ver EnviarVerificacionAsync).
        await correoCuenta.EnviarVerificacionAsync(user);

        return CreateAuthResponse(user, company.Id, company.LegalName, Role.Administrator);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Bloqueada: ni se mira la contraseña, así que seguir probando no sirve de nada.
        if (intentosLogin.BloqueadaHasta(normalizedEmail) is { } hasta)
        {
            var minutos = (int)Math.Ceiling((hasta - reloj.GetUtcNow()).TotalMinutes);
            throw new CuentaBloqueadaException(minutos);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.IsActive);
        var verificado = user ?? UsuarioFicticio;
        var resultado = PasswordHasher.VerifyHashedPassword(verificado, verificado.PasswordHash, request.Password);

        if (user is null || resultado == PasswordVerificationResult.Failed)
        {
            intentosLogin.RegistrarFallo(normalizedEmail);
            throw new InvalidCredentialsException();
        }

        intentosLogin.RegistrarExito(normalizedEmail);

        // Empresa con la que se inicia sesión: la primera membresía activa.
        var membership = await db.CompanyMemberships
            .Where(m => m.UserId == user.Id && m.IsActive && m.Company.IsActive)
            .OrderBy(m => m.Id)
            .Select(m => new { m.Company.Id, m.Company.LegalName, Rol = m.Role.Name })
            .FirstOrDefaultAsync();

        if (membership is null)
        {
            throw new InvalidCredentialsException();
        }

        user.LastAccessAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return CreateAuthResponse(user, membership.Id, membership.LegalName, membership.Rol);
    }

    /// <summary>
    /// Cambia la contraseña de la persona autenticada. Pide la actual para que una sesión
    /// abierta y sin dueño no alcance para apoderarse de la cuenta. Es también la única
    /// forma de reemplazar una contraseña provisional (ver EmpresaService.AgregarUsuarioAsync):
    /// esa nace válida pero la conoce quien dio el alta, así que conviene cambiarla.
    ///
    /// La contraseña nueva cambia el sello de los tokens (JwtTokenService.Sello): quedan fuera
    /// todas las sesiones abiertas, también esta. Por eso devuelve un token nuevo para la misma
    /// empresa activa, y quien cambió la contraseña sigue dentro.
    /// </summary>
    public async Task<AuthResponse> CambiarPasswordAsync(long userId, long companyId, CambiarPasswordRequest request)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new InvalidCredentialsException();

        var verificacion = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.PasswordActual);
        if (verificacion == PasswordVerificationResult.Failed)
        {
            throw new PasswordActualIncorrectaException();
        }

        if (PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.PasswordNueva)
            != PasswordVerificationResult.Failed)
        {
            throw new PasswordRepetidaException();
        }

        user.PasswordHash = PasswordHasher.HashPassword(user, request.PasswordNueva);
        await db.SaveChangesAsync();

        await correoCuenta.AvisarPasswordCambiadaAsync(user);

        return await CambiarEmpresaActivaAsync(userId, companyId);
    }

    /// <summary>
    /// Cambia la empresa activa emitiendo un token nuevo. Los permisos no se guardan en el
    /// token: al cambiar de empresa, el siguiente request los recalcula desde usuario_empresa.
    /// </summary>
    public async Task<AuthResponse> CambiarEmpresaActivaAsync(long userId, long companyId)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new InvalidCredentialsException();

        var membership = await db.CompanyMemberships
            .Where(m => m.UserId == userId && m.CompanyId == companyId && m.IsActive && m.Company.IsActive)
            .Select(m => new { m.Company.Id, m.Company.LegalName, Rol = m.Role.Name })
            .SingleOrDefaultAsync()
            ?? throw new MembresiaNoEncontradaException();

        return CreateAuthResponse(user, membership.Id, membership.LegalName, membership.Rol);
    }

    private AuthResponse CreateAuthResponse(User user, long companyId, string businessName, string rol)
    {
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user.Id, companyId, user.PasswordHash);
        return new AuthResponse(token, expiresAtUtc, businessName, user.Email, rol);
    }

    private static User CrearUsuarioFicticio()
    {
        var usuario = new User { Name = string.Empty, Email = string.Empty, PasswordHash = string.Empty };
        usuario.PasswordHash = PasswordHasher.HashPassword(usuario, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        return usuario;
    }
}
