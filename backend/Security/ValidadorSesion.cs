using System.Security.Claims;
using Backend.Data;
using Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.Security;

/// <summary>
/// Revisa en cada request autenticado que el token siga valiendo para el usuario: que exista,
/// esté activo y que su contraseña sea la misma que cuando se emitió el token (el sello, ver
/// JwtTokenService.Sello). Corre en OnTokenValidated del JWT, así cubre también los endpoints
/// con [Authorize] a secas, que no pasan por PermisoAuthorizationHandler.
///
/// Sin esto un token robado seguía sirviendo hasta vencer aunque la víctima cambiara la
/// contraseña, y un usuario desactivado conservaba el acceso mientras tuviera el token.
/// </summary>
public class ValidadorSesion(AppDbContext db, JwtTokenService jwtTokenService)
{
    public async Task<bool> EsValidaAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        long userId;
        try
        {
            userId = principal.GetUserId();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }

        var usuario = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.PasswordHash })
            .SingleOrDefaultAsync(ct);

        // Un token de antes del sello no trae el claim: se rechaza y obliga a entrar de nuevo.
        return usuario is { IsActive: true }
               && jwtTokenService.SelloValido(principal.FindFirstValue(JwtTokenService.ClaimSello), usuario.PasswordHash);
    }
}
