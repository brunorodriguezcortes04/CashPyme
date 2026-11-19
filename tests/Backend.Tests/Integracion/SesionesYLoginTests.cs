using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Backend.Security;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Sesiones que se pueden revocar (el sello de la contraseña en el JWT), bloqueo del login por
/// cuenta y tokens de correo de un solo uso aunque lleguen dos peticiones a la vez.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class SesionesYLoginTests(BaseDePruebas baseDePruebas)
{
    private const string Clave = "ClaveDePrueba123";

    [SkippableFact]
    public async Task Cambiar_la_contrasena_deja_fuera_los_tokens_anteriores_y_entrega_uno_nuevo()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var (email, empresaId, userId) = await RegistrarAsync(scope.ServiceProvider);
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var validador = scope.ServiceProvider.GetRequiredService<ValidadorSesion>();

        var viejo = (await auth.LoginAsync(new LoginRequest(email, Clave, false))).Token;
        Assert.True(await validador.EsValidaAsync(Principal(viejo)));

        var nueva = await auth.CambiarPasswordAsync(userId, empresaId, new CambiarPasswordRequest(Clave, "OtraClave456"));

        Assert.False(await validador.EsValidaAsync(Principal(viejo)));
        Assert.True(await validador.EsValidaAsync(Principal(nueva.Token)));
        Assert.Equal(empresaId.ToString(), Principal(nueva.Token).FindFirstValue("empresa_id"));
    }

    [SkippableFact]
    public async Task Restablecer_la_contrasena_deja_fuera_los_tokens_anteriores()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var (email, _, userId) = await RegistrarAsync(scope.ServiceProvider);
        var viejo = (await scope.ServiceProvider.GetRequiredService<AuthService>()
            .LoginAsync(new LoginRequest(email, Clave, false))).Token;

        var token = await TokenDeResetAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), userId);
        await scope.ServiceProvider.GetRequiredService<CorreoCuentaService>().RestablecerPasswordAsync(token, "OtraClave456");

        using var otro = sp.CreateScope();
        var validador = otro.ServiceProvider.GetRequiredService<ValidadorSesion>();
        var nuevo = (await otro.ServiceProvider.GetRequiredService<AuthService>()
            .LoginAsync(new LoginRequest(email, "OtraClave456", false))).Token;
        Assert.False(await validador.EsValidaAsync(Principal(viejo)));
        Assert.True(await validador.EsValidaAsync(Principal(nuevo)));
    }

    [SkippableFact]
    public async Task Un_usuario_desactivado_pierde_la_sesion_al_instante()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var (email, _, userId) = await RegistrarAsync(scope.ServiceProvider);
        var token = (await scope.ServiceProvider.GetRequiredService<AuthService>()
            .LoginAsync(new LoginRequest(email, Clave, false))).Token;

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

        Assert.False(await scope.ServiceProvider.GetRequiredService<ValidadorSesion>().EsValidaAsync(Principal(token)));
    }

    [SkippableFact]
    public async Task Un_token_sin_sello_se_rechaza()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var (_, empresaId, userId) = await RegistrarAsync(scope.ServiceProvider);

        // Así eran los tokens antes del sello.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim("empresa_id", empresaId.ToString())], "jwt"));

        Assert.False(await scope.ServiceProvider.GetRequiredService<ValidadorSesion>().EsValidaAsync(principal));
    }

    [SkippableFact]
    public async Task Tras_cinco_fallos_la_cuenta_se_bloquea_aunque_despues_acierte()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var (email, _, _) = await RegistrarAsync(scope.ServiceProvider);
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

        for (var i = 0; i < IntentosLoginService.FallosAntesDeBloquear; i++)
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(
                () => auth.LoginAsync(new LoginRequest(email, "equivocada", false)));
        }

        var bloqueo = await Assert.ThrowsAsync<CuentaBloqueadaException>(
            () => auth.LoginAsync(new LoginRequest(email, Clave, false)));
        Assert.Equal(429, bloqueo.StatusCode);
    }

    [SkippableFact]
    public async Task Un_correo_que_no_existe_se_bloquea_igual_y_no_delata_nada()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var inexistente = $"{Guid.NewGuid():N}@nadie.cl";

        for (var i = 0; i < IntentosLoginService.FallosAntesDeBloquear; i++)
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(
                () => auth.LoginAsync(new LoginRequest(inexistente, Clave, false)));
        }

        await Assert.ThrowsAsync<CuentaBloqueadaException>(
            () => auth.LoginAsync(new LoginRequest(inexistente, Clave, false)));
    }

    [SkippableFact]
    public async Task El_mismo_enlace_de_restablecimiento_no_sirve_dos_veces_aunque_lleguen_a_la_vez()
    {
        await using var sp = baseDePruebas.CrearServicios();
        string token;
        using (var scope = sp.CreateScope())
        {
            var (_, _, userId) = await RegistrarAsync(scope.ServiceProvider);
            token = await TokenDeResetAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), userId);
        }

        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        var resultados = await Task.WhenAll(
            Intentar(() => a.ServiceProvider.GetRequiredService<CorreoCuentaService>().RestablecerPasswordAsync(token, "ClaveDeA1234")),
            Intentar(() => b.ServiceProvider.GetRequiredService<CorreoCuentaService>().RestablecerPasswordAsync(token, "ClaveDeB1234")));

        Assert.Single(resultados, r => r is null);
        Assert.IsType<TokenInvalidoException>(Assert.Single(resultados, r => r is not null));
    }

    private static async Task<Exception?> Intentar(Func<Task> accion)
    {
        try
        {
            await accion();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<(string Email, long EmpresaId, long UserId)> RegistrarAsync(IServiceProvider sp)
    {
        var (empresaId, userId) = await baseDePruebas.RegistrarEmpresaAsync(sp, "PRUEBA sesiones");
        var email = await sp.GetRequiredService<AppDbContext>().Users
            .Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
        return (email, empresaId, userId);
    }

    /// <summary>Un enlace de restablecimiento como el del correo, con su token en claro.</summary>
    private static async Task<string> TokenDeResetAsync(AppDbContext db, long userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.TokensUsuario.Add(new TokenUsuario
        {
            UserId = userId,
            Type = TokenUsuario.ResetPassword,
            TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await db.SaveChangesAsync();
        return token;
    }

    /// <summary>Los claims del token tal como los deja el middleware (MapInboundClaims = false).</summary>
    private static ClaimsPrincipal Principal(string token)
        => new(new ClaimsIdentity(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims, "jwt"));
}
