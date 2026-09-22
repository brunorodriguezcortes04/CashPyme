using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Services;

public class AuthService(AppDbContext db, JwtTokenService jwtTokenService)
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

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

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dos registros simultáneos con el mismo correo (ux_usuario_email).
            throw new EmailAlreadyRegisteredException();
        }

        return CreateAuthResponse(user, company.Id, company.LegalName);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.IsActive);

        if (user is null || PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            throw new InvalidCredentialsException();
        }

        // Empresa con la que se inicia sesión: la primera membresía activa.
        var company = await db.CompanyMemberships
            .Where(m => m.UserId == user.Id && m.IsActive && m.Company.IsActive)
            .OrderBy(m => m.Id)
            .Select(m => new { m.Company.Id, m.Company.LegalName })
            .FirstOrDefaultAsync();

        if (company is null)
        {
            throw new InvalidCredentialsException();
        }

        user.LastAccessAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return CreateAuthResponse(user, company.Id, company.LegalName);
    }

    private AuthResponse CreateAuthResponse(User user, long companyId, string businessName)
    {
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user.Id, user.Email, companyId, businessName);
        return new AuthResponse(token, expiresAtUtc, businessName, user.Email);
    }
}
