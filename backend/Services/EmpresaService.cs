using System.Security.Cryptography;
using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class EmpresaService(AppDbContext db)
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async Task<EmpresaResponse> ObtenerAsync(long companyId)
    {
        var empresa = await db.Companies
            .Where(c => c.Id == companyId && c.IsActive)
            .SingleOrDefaultAsync()
            ?? throw new EmpresaNoEncontradaException();

        return ToResponse(empresa);
    }

    public async Task<EmpresaResponse> ActualizarAsync(long companyId, UpdateEmpresaRequest request)
    {
        var empresa = await db.Companies
            .Where(c => c.Id == companyId && c.IsActive)
            .SingleOrDefaultAsync()
            ?? throw new EmpresaNoEncontradaException();

        empresa.LegalName = request.RazonSocial.Trim();
        // Se guarda normalizado (12345678-5): es el único formato que acepta ck_empresa_rut,
        // y deja que ux_empresa_rut compare siempre el mismo texto.
        empresa.Rut = string.IsNullOrWhiteSpace(request.Rut) ? null : Dominio.Rut.Normalizar(request.Rut);
        empresa.BusinessActivity = request.Giro?.Trim();
        empresa.Address = request.Direccion?.Trim();
        empresa.Phone = request.Telefono?.Trim();
        empresa.ContactEmail = request.EmailContacto?.Trim();
        empresa.DueDateWarningDays = request.DiasAvisoVencimiento;
        empresa.LowBalanceThreshold = request.UmbralSaldoBajo;

        // El único índice único que esta actualización puede violar es ux_empresa_rut.
        await db.SaveChangesTraducidoAsync(() => new RutDuplicadoException());

        return ToResponse(empresa);
    }

    public async Task<IReadOnlyList<EmpresaMembresiaResponse>> ListarMisEmpresasAsync(long userId, long companyIdActiva)
    {
        return await db.CompanyMemberships
            .Where(m => m.UserId == userId && m.IsActive && m.Company.IsActive)
            .OrderBy(m => m.Company.LegalName)
            .Select(m => new EmpresaMembresiaResponse(
                m.CompanyId,
                m.Company.LegalName,
                m.Role.Name,
                m.CompanyId == companyIdActiva))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<UsuarioEmpresaResponse>> ListarUsuariosAsync(long companyId)
    {
        return await db.CompanyMemberships
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.User.Name)
            .Select(m => new UsuarioEmpresaResponse(
                m.UserId,
                m.User.Name,
                m.User.Email,
                m.Role.Name,
                m.IsActive))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<RolResponse>> ListarRolesAsync()
    {
        return await db.Roles
            .OrderBy(r => r.Id)
            .Select(r => new RolResponse(r.Id, r.Name))
            .ToListAsync();
    }

    /// <summary>
    /// Da acceso a una persona a la empresa activa con un rol. Dos caminos según el correo:
    /// si ya tiene cuenta en CashPyme solo se crea la membresía (conserva su contraseña); si
    /// no, se crea el usuario con una contraseña provisional que se devuelve una única vez,
    /// porque todavía no hay envío de correo para invitarla por su cuenta.
    /// </summary>
    public async Task<UsuarioCreadoResponse> AgregarUsuarioAsync(
        long companyId,
        CrearUsuarioEmpresaRequest request)
    {
        var rol = await db.Roles.SingleOrDefaultAsync(r => r.Id == request.IdRol)
            ?? throw new RolNoEncontradoException();

        var email = request.Email.Trim().ToLowerInvariant();
        var existente = await db.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == email);

        if (existente is not null)
        {
            return await DarAccesoAUsuarioExistenteAsync(companyId, existente, rol);
        }

        var passwordProvisional = GenerarPasswordProvisional();
        var usuario = new User
        {
            Name = request.Nombre.Trim(),
            Email = email,
            PasswordHash = string.Empty
        };
        usuario.PasswordHash = PasswordHasher.HashPassword(usuario, passwordProvisional);

        db.Users.Add(usuario);
        db.CompanyMemberships.Add(new CompanyMembership
        {
            User = usuario,
            CompanyId = companyId,
            RoleId = rol.Id
        });

        // Dos altas simultáneas con el mismo correo (ux_usuario_email).
        await db.SaveChangesTraducidoAsync(() => new UsuarioYaEnEmpresaException());

        return new UsuarioCreadoResponse(ToResponse(usuario, rol.Name, activo: true), passwordProvisional);
    }

    /// <summary>
    /// La persona ya tiene cuenta en CashPyme: acá solo se le abre esta empresa. No se
    /// devuelve contraseña provisional porque sigue entrando con la suya de siempre.
    /// </summary>
    private async Task<UsuarioCreadoResponse> DarAccesoAUsuarioExistenteAsync(
        long companyId,
        User usuario,
        Role rol)
    {
        var membresia = await db.CompanyMemberships
            .SingleOrDefaultAsync(m => m.UserId == usuario.Id && m.CompanyId == companyId);

        if (membresia is { IsActive: true })
        {
            throw new UsuarioYaEnEmpresaException();
        }

        if (membresia is null)
        {
            db.CompanyMemberships.Add(new CompanyMembership
            {
                UserId = usuario.Id,
                CompanyId = companyId,
                RoleId = rol.Id
            });
        }
        else
        {
            // Reactivar en vez de crear otra membresía: uq_usuario_empresa no deja tener dos
            // filas para el mismo par usuario-empresa.
            membresia.IsActive = true;
            membresia.RoleId = rol.Id;
        }

        await db.SaveChangesTraducidoAsync(() => new UsuarioYaEnEmpresaException());

        return new UsuarioCreadoResponse(ToResponse(usuario, rol.Name, activo: true), null);
    }

    public async Task<UsuarioEmpresaResponse> CambiarRolAsync(long companyId, long idUsuario, short idRol)
    {
        var rol = await db.Roles.SingleOrDefaultAsync(r => r.Id == idRol)
            ?? throw new RolNoEncontradoException();

        var membresia = await BuscarMembresiaAsync(companyId, idUsuario);

        if (rol.Name != Role.Administrator)
        {
            await AsegurarNoEsUltimoAdministradorAsync(companyId, membresia);
        }

        membresia.RoleId = rol.Id;
        await db.SaveChangesAsync();

        return ToResponse(membresia.User, rol.Name, membresia.IsActive);
    }

    public async Task<UsuarioEmpresaResponse> CambiarEstadoUsuarioAsync(long companyId, long idUsuario, bool activo)
    {
        var membresia = await BuscarMembresiaAsync(companyId, idUsuario);

        if (!activo)
        {
            await AsegurarNoEsUltimoAdministradorAsync(companyId, membresia);
        }

        membresia.IsActive = activo;
        await db.SaveChangesAsync();

        return ToResponse(membresia.User, membresia.Role.Name, activo);
    }

    private async Task<CompanyMembership> BuscarMembresiaAsync(long companyId, long idUsuario)
    {
        return await db.CompanyMemberships
            .Include(m => m.User)
            .Include(m => m.Role)
            .SingleOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == idUsuario)
            ?? throw new UsuarioNoEnEmpresaException();
    }

    /// <summary>
    /// Bloquea quitarle el rol o el acceso al último Administrador activo: sin ninguno, la
    /// empresa queda sin quien administre usuarios ni configuración, y no se puede deshacer
    /// desde la aplicación.
    /// </summary>
    private async Task AsegurarNoEsUltimoAdministradorAsync(long companyId, CompanyMembership membresia)
    {
        if (membresia.Role.Name != Role.Administrator || !membresia.IsActive)
        {
            return;
        }

        var otrosAdministradores = await db.CompanyMemberships
            .CountAsync(m => m.CompanyId == companyId
                && m.UserId != membresia.UserId
                && m.IsActive
                && m.Role.Name == Role.Administrator);

        if (otrosAdministradores == 0)
        {
            throw new UltimoAdministradorException();
        }
    }

    /// <summary>
    /// Contraseña provisional legible para dictarla por teléfono o mensaje: sin caracteres
    /// que se confundan entre sí (O/0, l/1/I). Se genera con el RNG criptográfico, no con
    /// Random, porque es una credencial real hasta que la persona la cambie.
    /// </summary>
    private static string GenerarPasswordProvisional()
    {
        const string alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var caracteres = new char[12];

        for (var i = 0; i < caracteres.Length; i++)
        {
            caracteres[i] = alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
        }

        return new string(caracteres);
    }

    private static UsuarioEmpresaResponse ToResponse(User usuario, string rol, bool activo)
        => new(usuario.Id, usuario.Name, usuario.Email, rol, activo);

    private static EmpresaResponse ToResponse(Models.Company c) => new(
        c.Id,
        c.LegalName,
        c.Rut,
        c.BusinessActivity,
        c.Address,
        c.Phone,
        c.ContactEmail,
        c.TimeZone,
        c.DueDateWarningDays,
        c.LowBalanceThreshold
    );
}
