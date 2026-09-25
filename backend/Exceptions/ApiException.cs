namespace Backend.Exceptions;

/// <summary>
/// Error de negocio que la API sabe convertir en respuesta HTTP: el mensaje va al cuerpo y
/// el status code viaja en la propia excepción.
///
/// Antes el mapeo vivía en un switch dentro de ApiExceptionHandler, lejos de la excepción.
/// Eso tenía un modo de falla silencioso: una excepción nueva sin su rama en el switch no
/// rompía la compilación, simplemente se escapaba como 500 genérico y solo se notaba
/// probando a mano. Heredando de acá el status es obligatorio para existir.
/// </summary>
public abstract class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
