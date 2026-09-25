using Microsoft.AspNetCore.Diagnostics;

namespace Backend.Exceptions;

/// <summary>
/// Traduce los errores de negocio a respuestas HTTP. Solo maneja ApiException: cualquier
/// otra excepción se deja pasar a propósito para que la trate el pipeline por defecto y no
/// se filtre al cliente un detalle interno disfrazado de error de negocio.
/// </summary>
public class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ApiException apiException)
        {
            return false;
        }

        httpContext.Response.StatusCode = apiException.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(new { message = apiException.Message }, cancellationToken);
        return true;
    }
}
