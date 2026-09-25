using System.Security.Cryptography;
using System.Text;
using Backend.Data;
using Backend.Exceptions;
using Backend.Models;
using Backend.Services.Correo;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <summary>
/// Flujos de la cuenta que pasan por el correo: verificar el email y recuperar la contraseña.
/// Ambos usan token_usuario con el mismo esquema: se genera un token aleatorio, se manda por
/// correo y en la base queda solo su hash SHA-256.
/// </summary>
public class CorreoCuentaService(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<CorreoCuentaService> logger)
{
    private const int HorasVigenciaVerificacion = 24;
    private const int MinutosVigenciaReset = 60;
    // Cuánto esperar antes de mandar otro correo del mismo tipo a la misma persona: evita
    // que alguien use "olvidé mi contraseña" para inundarle la bandeja a un tercero.
    private static readonly TimeSpan EsperaEntreCorreos = TimeSpan.FromMinutes(2);

    private static readonly PasswordHasher<User> PasswordHasher = new();

    private string FrontendUrl => appOptions.Value.FrontendUrl.TrimEnd('/');

    // ------------------------------------------------------------------ verificación

    /// <summary>
    /// Envía el enlace de verificación. No lanza si el correo falla: el registro ya quedó
    /// guardado y la persona puede pedir otro enlace después; tumbar el registro por un
    /// problema del proveedor de correo sería peor.
    /// </summary>
    public async Task EnviarVerificacionAsync(User user, CancellationToken ct = default)
    {
        try
        {
            var token = await CrearTokenAsync(user.Id, TokenUsuario.VerificacionEmail,
                TimeSpan.FromHours(HorasVigenciaVerificacion), ct);
            var enlace = $"{FrontendUrl}/verificar-email?token={token}";
            await emailSender.EnviarAsync(
                PlantillasCorreo.VerificacionEmail(user.Email, user.Name, enlace, HorasVigenciaVerificacion), ct);
        }
        catch (Exception ex) when (ex is EmailNoEnviadoException or DbUpdateException)
        {
            logger.LogError(ex, "No se pudo enviar la verificación de correo al usuario {UserId}.", user.Id);
        }
    }

    public async Task ReenviarVerificacionAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new InvalidCredentialsException();

        if (user.EmailVerifiedAtUtc is not null)
        {
            throw new EmailYaVerificadoException();
        }

        if (await EnviadoHacePocoAsync(user.Id, TokenUsuario.VerificacionEmail, ct))
        {
            return;
        }

        await EnviarVerificacionAsync(user, ct);
    }

    public async Task VerificarEmailAsync(string token, CancellationToken ct = default)
    {
        var registro = await BuscarTokenVigenteAsync(token, TokenUsuario.VerificacionEmail, ct);

        registro.UsedAtUtc = DateTime.UtcNow;
        registro.User.EmailVerifiedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ recuperación

    /// <summary>
    /// Siempre termina "bien", exista o no el correo: la respuesta no puede revelar qué
    /// correos están registrados (mismo criterio que InvalidCredentialsException).
    /// </summary>
    public async Task SolicitarResetAsync(string email, CancellationToken ct = default)
    {
        var normalizado = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == normalizado && u.IsActive, ct);

        if (user is null || await EnviadoHacePocoAsync(user.Id, TokenUsuario.ResetPassword, ct))
        {
            return;
        }

        try
        {
            var token = await CrearTokenAsync(user.Id, TokenUsuario.ResetPassword,
                TimeSpan.FromMinutes(MinutosVigenciaReset), ct);
            var enlace = $"{FrontendUrl}/restablecer-password?token={token}";
            await emailSender.EnviarAsync(
                PlantillasCorreo.RecuperarPassword(user.Email, user.Name, enlace, MinutosVigenciaReset), ct);
        }
        catch (EmailNoEnviadoException ex)
        {
            // Se loguea y no se propaga: un error distinto delataría que el correo existe.
            logger.LogError(ex, "No se pudo enviar el correo de recuperación al usuario {UserId}.", user.Id);
        }
    }

    public async Task RestablecerPasswordAsync(string token, string passwordNueva, CancellationToken ct = default)
    {
        var registro = await BuscarTokenVigenteAsync(token, TokenUsuario.ResetPassword, ct);
        var user = registro.User;

        if (!user.IsActive)
        {
            throw new TokenInvalidoException();
        }

        user.PasswordHash = PasswordHasher.HashPassword(user, passwordNueva);
        // Abrir el enlace prueba que la bandeja es suya: de paso queda verificado el correo.
        user.EmailVerifiedAtUtc ??= DateTime.UtcNow;
        registro.UsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await AvisarPasswordCambiadaAsync(user, ct);
    }

    /// <summary>
    /// Aviso de seguridad tras cualquier cambio de contraseña. Si falla solo se loguea:
    /// la contraseña ya cambió y eso no se puede deshacer por un correo que no salió.
    /// </summary>
    public async Task AvisarPasswordCambiadaAsync(User user, CancellationToken ct = default)
    {
        try
        {
            await emailSender.EnviarAsync(
                PlantillasCorreo.PasswordCambiada(user.Email, user.Name, $"{FrontendUrl}/olvide-password"), ct);
        }
        catch (EmailNoEnviadoException ex)
        {
            logger.LogError(ex, "No se pudo enviar el aviso de cambio de contraseña al usuario {UserId}.", user.Id);
        }
    }

    // ------------------------------------------------------------------ tokens

    /// <summary>
    /// Crea el token y anula los anteriores del mismo tipo que sigan sin usar: solo el enlace
    /// del último correo sirve, así un correo viejo reenviado o filtrado ya no funciona.
    /// </summary>
    private async Task<string> CrearTokenAsync(long userId, string tipo, TimeSpan vigencia, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        await db.TokensUsuario
            .Where(t => t.UserId == userId && t.Type == tipo && t.UsedAtUtc == null && t.ExpiresAtUtc > ahora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, ahora), ct);

        // 32 bytes aleatorios = 256 bits: imposible de adivinar. Base64Url para que viaje en la URL.
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        db.TokensUsuario.Add(new TokenUsuario
        {
            UserId = userId,
            Type = tipo,
            TokenHash = Hash(token),
            ExpiresAtUtc = ahora.Add(vigencia)
        });
        await db.SaveChangesAsync(ct);

        return token;
    }

    private async Task<TokenUsuario> BuscarTokenVigenteAsync(string token, string tipo, CancellationToken ct)
    {
        var hash = Hash(token.Trim());
        var ahora = DateTime.UtcNow;

        return await db.TokensUsuario
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash
                                       && t.Type == tipo
                                       && t.UsedAtUtc == null
                                       && t.ExpiresAtUtc > ahora, ct)
            ?? throw new TokenInvalidoException();
    }

    private Task<bool> EnviadoHacePocoAsync(long userId, string tipo, CancellationToken ct)
    {
        var limite = DateTime.UtcNow - EsperaEntreCorreos;
        return db.TokensUsuario.AnyAsync(t => t.UserId == userId && t.Type == tipo && t.CreatedAtUtc > limite, ct);
    }

    /// <summary>SHA-256 en hexadecimal: 64 caracteres, lo que espera token_usuario.token_hash.</summary>
    private static string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
