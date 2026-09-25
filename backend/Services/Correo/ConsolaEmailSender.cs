namespace Backend.Services.Correo;

/// <summary>
/// Reemplazo de Resend cuando no hay API key configurada: en vez de enviar, escribe el correo
/// en el log. Sirve para desarrollar y probar los flujos (el enlace de verificación o de
/// recuperación aparece en la consola de "dotnet run") sin gastar envíos ni tener cuenta.
/// </summary>
public class ConsolaEmailSender(ILogger<ConsolaEmailSender> logger) : IEmailSender
{
    public Task EnviarAsync(CorreoSaliente correo, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "[CORREO NO ENVIADO — falta Email:ResendApiKey]\nPara: {Para}\nAsunto: {Asunto}\n{Texto}",
            correo.Para, correo.Asunto, correo.Texto);
        return Task.CompletedTask;
    }
}
