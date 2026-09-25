using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Security;

/// <summary>
/// Límites de frecuencia para los endpoints que mandan correos o reciben tokens por correo.
/// Sin esto, cualquiera podría usar "olvidé mi contraseña" para gastar la cuota de Resend
/// o probar tokens a fuerza bruta.
/// </summary>
public static class RateLimits
{
    public const string Correo = "correo";

    public static IServiceCollection AddCashPymeRateLimits(this IServiceCollection services)
    {
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Hiciste demasiados intentos. Espera unos minutos e inténtalo de nuevo." }, ct);

            // 10 solicitudes cada 15 minutos por IP, sumando todos los endpoints de correo.
            options.AddPolicy(Correo, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));
        });
    }
}
