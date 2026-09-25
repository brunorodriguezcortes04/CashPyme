namespace Backend.Services.Correo;

public record CorreoSaliente(string Para, string Asunto, string Html, string Texto);

/// <summary>
/// Envío de correos. Los servicios dependen de esta interfaz y no de Resend: así en
/// desarrollo se puede usar ConsolaEmailSender y cambiar de proveedor no toca la lógica.
/// </summary>
public interface IEmailSender
{
    /// <summary>Lanza EmailNoEnviadoException si el proveedor rechaza o no responde.</summary>
    Task EnviarAsync(CorreoSaliente correo, CancellationToken cancellationToken = default);
}

public class EmailNoEnviadoException(string message, Exception? inner = null) : Exception(message, inner);
