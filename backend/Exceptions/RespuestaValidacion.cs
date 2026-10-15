using Microsoft.AspNetCore.Mvc;

namespace Backend.Exceptions;

/// <summary>
/// Errores de validación del request ([Range], [RutValido], [EmailAddress]…) con el mismo
/// formato que los de negocio ({ message }), para que el front muestre el mensaje tal cual.
/// Sin esto salía el ValidationProblemDetails por defecto, que el front no lee, y la persona
/// veía un "revisa los datos" genérico. Así el front no necesita repetir estas reglas.
/// </summary>
public static class RespuestaValidacion
{
    public const string MensajeGenerico = "Revisa los datos ingresados e inténtalo de nuevo.";

    public static IActionResult Crear(ActionContext contexto)
        => new BadRequestObjectResult(new { message = Mensaje(contexto.ModelState) });

    public static string Mensaje(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
    {
        // Las claves que empiezan con "$" son de JSON mal formado (un texto donde va un número):
        // su mensaje es técnico y en inglés, no algo que la persona pueda corregir leyéndolo.
        if (modelState.Keys.Any(clave => clave.StartsWith('$')))
        {
            return MensajeGenerico;
        }

        var mensajes = modelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();

        return mensajes.Count > 0 ? string.Join(" ", mensajes) : MensajeGenerico;
    }
}
