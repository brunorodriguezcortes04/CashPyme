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

/// <summary>
/// Permanente: reintentar no sirve (dirección inválida, destinatario no permitido en modo
/// prueba). Los demás (proveedor caído, límite de envíos) se reintentan en el próximo ciclo.
/// </summary>
public class EmailNoEnviadoException(string message, Exception? inner = null, bool permanente = false)
    : Exception(message, inner)
{
    public bool Permanente { get; } = permanente;
}
