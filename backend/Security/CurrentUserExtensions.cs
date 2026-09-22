using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Backend.Security;

/// <summary>Lee la empresa y el usuario actuales desde los claims del JWT (ver JwtTokenService).</summary>
public static class CurrentUserExtensions
{
    public static long GetCompanyId(this ClaimsPrincipal user)
        => long.Parse(user.FindFirstValue("empresa_id")
            ?? throw new InvalidOperationException("El token no contiene el claim 'empresa_id'."));

    public static long GetUserId(this ClaimsPrincipal user)
        => long.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("El token no contiene el claim 'sub'."));
}
