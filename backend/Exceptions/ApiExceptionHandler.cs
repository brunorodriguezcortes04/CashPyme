using Microsoft.AspNetCore.Diagnostics;

namespace Backend.Exceptions;

public class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            EmailAlreadyRegisteredException => StatusCodes.Status409Conflict,
            InvalidCredentialsException => StatusCodes.Status401Unauthorized,
            CuentaNoEncontradaException => StatusCodes.Status404NotFound,
            TerceroNoEncontradoException => StatusCodes.Status404NotFound,
            CategoriaInvalidaException => StatusCodes.Status400BadRequest,
            CuentaDuplicadaException => StatusCodes.Status409Conflict,
            MembresiaNoEncontradaException => StatusCodes.Status403Forbidden,
            EmpresaNoEncontradaException => StatusCodes.Status404NotFound,
            _ => 0
        };

        if (statusCode == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new { message = exception.Message }, cancellationToken);
        return true;
    }
}
