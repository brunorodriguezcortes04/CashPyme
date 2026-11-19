using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Security;

/// <summary>
/// Límites de frecuencia por IP para los endpoints que no exigen sesión: los que mandan
/// correos o reciben tokens por correo, el login y el registro. Sin esto, cualquiera podría
/// usar "olvidé mi contraseña" para gastar la cuota de Resend, probar tokens o contraseñas a
/// fuerza bruta, o crear empresas sin fin.
///
/// El límite por IP es la primera barrera. El login además bloquea por cuenta
/// (IntentosLoginService), que no depende de la IP: un atacante con muchas IPs se topa con ese.
/// </summary>
public static class RateLimits
{
    public const string Correo = "correo";
    public const string Login = "login";
    public const string Registro = "registro";

    /// <summary>
    /// Encabezado que pone Cloudflare con la IP del cliente. Render atiende siempre detrás de
    /// Cloudflare, y su proxy NO limpia el X-Forwarded-For que manda el cliente, solo le agrega
    /// valores: tomarlo de ahí permitía estrenar una IP inventada en cada petición y saltarse
    /// el límite. Cloudflare sobrescribe CF-Connecting-IP en cada petición, así que el cliente
    /// no lo puede elegir.
    /// </summary>
    private const string EncabezadoIpCloudflare = "CF-Connecting-IP";

    public static IServiceCollection AddCashPymeRateLimits(this IServiceCollection services, bool detrasDeCloudflare)
    {
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Hiciste demasiados intentos. Espera unos minutos e inténtalo de nuevo." }, ct);
            };

            // 10 solicitudes cada 15 minutos por IP, sumando todos los endpoints de correo.
            options.AddPolicy(Correo, httpContext => PorIp(httpContext, detrasDeCloudflare, 10, TimeSpan.FromMinutes(15)));

            // 20 cada 15 minutos: una oficina entera puede salir por la misma IP, así que no
            // puede ser tan bajo como el bloqueo por cuenta (5 fallos).
            options.AddPolicy(Login, httpContext => PorIp(httpContext, detrasDeCloudflare, 20, TimeSpan.FromMinutes(15)));

            // Cada registro crea empresa, usuario, categorías y cuenta y manda un correo.
            options.AddPolicy(Registro, httpContext => PorIp(httpContext, detrasDeCloudflare, 5, TimeSpan.FromHours(1)));
        });
    }

    /// <summary>
    /// IP del cliente para agrupar los límites. En producción la de Cloudflare; sin ese
    /// encabezado (desarrollo, o una petición que no pasó por Cloudflare) la de la conexión.
    /// </summary>
    public static string IpCliente(HttpContext httpContext, bool detrasDeCloudflare)
    {
        if (detrasDeCloudflare
            && IPAddress.TryParse(httpContext.Request.Headers[EncabezadoIpCloudflare].ToString(), out var ip))
        {
            return ip.ToString();
        }
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
    }

    private static RateLimitPartition<string> PorIp(
        HttpContext httpContext, bool detrasDeCloudflare, int permitidas, TimeSpan ventana)
        => RateLimitPartition.GetFixedWindowLimiter(
            IpCliente(httpContext, detrasDeCloudflare),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitidas,
                Window = ventana,
                QueueLimit = 0
            });
}
