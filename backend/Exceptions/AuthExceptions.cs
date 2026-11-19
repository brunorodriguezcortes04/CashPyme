namespace Backend.Exceptions;

/// <summary>
/// Un solo mensaje para token inexistente, vencido o ya usado: distinguirlos no le sirve a
/// la persona (en los tres casos tiene que pedir otro enlace) y sí le serviría a quien
/// intenta adivinar tokens.
/// </summary>
public class TokenInvalidoException()
    : ApiException(StatusCodes.Status400BadRequest, "El enlace no es válido o ya expiró. Solicita uno nuevo.");

public class EmailYaVerificadoException()
    : ApiException(StatusCodes.Status409Conflict, "Tu correo ya está verificado.");

public class EmailAlreadyRegisteredException()
    : ApiException(StatusCodes.Status409Conflict, "Ya existe una cuenta con este correo electrónico.");

/// <summary>
/// Mensaje deliberadamente ambiguo: no revela si falló el correo o la contraseña, para no
/// confirmarle a nadie qué correos están registrados.
/// </summary>
public class InvalidCredentialsException()
    : ApiException(StatusCodes.Status401Unauthorized, "Correo o contraseña incorrectos.");

/// <summary>
/// A diferencia del login, acá sí se puede decir qué falló: la persona ya está autenticada,
/// así que no se le revela nada que no sepa, y un mensaje genérico solo la confundiría.
/// </summary>
public class PasswordActualIncorrectaException()
    : ApiException(StatusCodes.Status400BadRequest, "La contraseña actual no es correcta.");

public class PasswordRepetidaException()
    : ApiException(StatusCodes.Status400BadRequest, "La contraseña nueva tiene que ser distinta de la actual.");

/// <summary>
/// La cuenta quedó bloqueada un rato por intentos fallidos (ver IntentosLoginService). Sale
/// igual exista o no el correo, para no revelar cuáles están registrados.
/// </summary>
public class CuentaBloqueadaException(int minutos)
    : ApiException(StatusCodes.Status429TooManyRequests,
        minutos <= 1
            ? "Hubo demasiados intentos fallidos con este correo. Espera un minuto e inténtalo de nuevo."
            : $"Hubo demasiados intentos fallidos con este correo. Espera {minutos} minutos e inténtalo de nuevo.");
