using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService authService, CorreoCuentaService correoCuenta) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(RateLimits.Registro)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var result = await authService.RegisterAsync(request);
        return Ok(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var result = await authService.LoginAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Cambia la contraseña propia. No pide permiso de la matriz: cualquier persona
    /// autenticada puede cambiar la suya, sea cual sea su rol en la empresa activa.
    /// Devuelve una sesión nueva: la contraseña nueva invalida todos los tokens anteriores,
    /// también el que trajo esta petición.
    /// </summary>
    [Authorize]
    [HttpPut("password")]
    public async Task<ActionResult<AuthResponse>> CambiarPassword(CambiarPasswordRequest request)
    {
        var result = await authService.CambiarPasswordAsync(User.GetUserId(), User.GetCompanyId(), request);
        return Ok(result);
    }

    /// <summary>Confirma el correo con el token que llegó en el enlace de verificación.</summary>
    [HttpPost("verificar-email")]
    [EnableRateLimiting(RateLimits.Correo)]
    public async Task<IActionResult> VerificarEmail(VerificarEmailRequest request)
    {
        await correoCuenta.VerificarEmailAsync(request.Token);
        return NoContent();
    }

    /// <summary>Manda otro enlace de verificación a la persona autenticada.</summary>
    [Authorize]
    [HttpPost("reenviar-verificacion")]
    [EnableRateLimiting(RateLimits.Correo)]
    public async Task<IActionResult> ReenviarVerificacion()
    {
        await correoCuenta.ReenviarVerificacionAsync(User.GetUserId());
        return NoContent();
    }

    /// <summary>
    /// Responde 204 exista o no el correo, para no revelar qué cuentas están registradas.
    /// </summary>
    [HttpPost("olvide-password")]
    [EnableRateLimiting(RateLimits.Correo)]
    public async Task<IActionResult> OlvidePassword(OlvidePasswordRequest request)
    {
        await correoCuenta.SolicitarResetAsync(request.Email);
        return NoContent();
    }

    [HttpPost("restablecer-password")]
    [EnableRateLimiting(RateLimits.Correo)]
    public async Task<IActionResult> RestablecerPassword(RestablecerPasswordRequest request)
    {
        await correoCuenta.RestablecerPasswordAsync(request.Token, request.PasswordNueva);
        return NoContent();
    }
}
