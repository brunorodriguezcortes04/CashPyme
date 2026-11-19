using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Services;

public class JwtOptions
{
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public required string Key { get; set; }
    public int ExpiresMinutes { get; set; } = 60;
}

public class JwtTokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    /// <summary>Claim con el sello de la contraseña (ver Sello).</summary>
    public const string ClaimSello = "sello";

    private readonly JwtOptions _options = options.Value;

    /// <summary>
    /// El JWT solo firma, no cifra: cualquiera con el token puede decodificar su payload
    /// (es base64, no un secreto). Por eso lleva únicamente lo que el backend necesita para
    /// autorizar (usuario, empresa activa y el sello) y nada que identifique a la persona o el
    /// negocio; correo, nombre de la empresa y rol viajan en el cuerpo de la respuesta, no en
    /// el token.
    /// </summary>
    public (string Token, DateTime ExpiresAtUtc) CreateToken(long userId, long companyId, string passwordHash)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("empresa_id", companyId.ToString()),
            new Claim(ClaimSello, Sello(passwordHash))
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }

    /// <summary>
    /// Huella de la contraseña actual que viaja en el token (la misma idea que el
    /// session_auth_hash de Django). Cada request la vuelve a calcular con el hash guardado
    /// (ValidadorSesion): cambiar o restablecer la contraseña cambia el hash, y con eso dejan de
    /// servir todos los tokens emitidos antes, sin guardar una lista de tokens revocados.
    ///
    /// Es un HMAC con la clave del JWT y no el hash mismo: el payload del token se lee sin
    /// clave, y no debe llevar nada que ayude a atacar la contraseña. 128 bits bastan para que
    /// no se pueda adivinar.
    /// </summary>
    public string Sello(string passwordHash)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.Key), Encoding.UTF8.GetBytes(passwordHash));
        return WebEncoders.Base64UrlEncode(mac, 0, 16);
    }

    /// <summary>Compara en tiempo constante, para no dar pistas del sello por lo que tarda.</summary>
    public bool SelloValido(string? selloDelToken, string passwordHash)
    {
        if (string.IsNullOrEmpty(selloDelToken))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(selloDelToken), Encoding.ASCII.GetBytes(Sello(passwordHash)));
    }
}
