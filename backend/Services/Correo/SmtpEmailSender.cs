using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Backend.Services.Correo;

/// <summary>
/// Envía por SMTP con MailKit (System.Net.Mail.SmtpClient está desaconsejado para código nuevo).
/// A diferencia de Resend sin dominio verificado, entrega a cualquier dirección: así les llegan
/// las alertas a todos los administradores y contadores, no solo al dueño de la cuenta.
/// </summary>
public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task EnviarAsync(CorreoSaliente correo, CancellationToken cancellationToken = default)
    {
        var smtp = _options.Smtp;
        var mensaje = new MimeMessage();
        mensaje.From.Add(Remitente(_options.From, smtp.Usuario));
        mensaje.To.Add(MailboxAddress.Parse(correo.Para));
        if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
        {
            mensaje.ReplyTo.Add(MailboxAddress.Parse(_options.ReplyTo));
        }
        mensaje.Subject = correo.Asunto;
        mensaje.Body = new BodyBuilder { HtmlBody = correo.Html, TextBody = correo.Texto }.ToMessageBody();

        using var cliente = new SmtpClient { Timeout = 15_000 };
        try
        {
            await cliente.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.Auto, cancellationToken);
            await cliente.AuthenticateAsync(smtp.Usuario ?? correo.Para, smtp.Password, cancellationToken);
            await cliente.SendAsync(mensaje, cancellationToken);
            await cliente.DisconnectAsync(true, cancellationToken);
        }
        catch (SmtpCommandException ex)
        {
            // 5xx sobre el destinatario (no existe, buzón rechazado) es el mismo rechazo en cada
            // reintento; el resto (4xx, problemas del remitente o de la cuenta) puede pasar solo.
            var permanente = ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted && (int)ex.StatusCode >= 500;
            throw new EmailNoEnviadoException($"El servidor SMTP respondió {(int)ex.StatusCode}: {ex.Message}", ex, permanente);
        }
        catch (Exception ex) when (ex is SmtpProtocolException or AuthenticationException
                                       or ServiceNotConnectedException or IOException or TimeoutException
                                       or System.Net.Sockets.SocketException)
        {
            // Conexión o credenciales: se reintenta (y se ve en el log) hasta que se arregle.
            throw new EmailNoEnviadoException("No se pudo enviar por SMTP.", ex);
        }

        logger.LogInformation("Correo \"{Asunto}\" enviado a {Para}.", correo.Asunto, correo.Para);
    }

    /// <summary>
    /// Gmail y Brevo solo aceptan como remitente la cuenta (o el remitente verificado). Si Email:From
    /// sigue siendo el de prueba de Resend, se usa el usuario SMTP con el nombre "CashPyme".
    /// </summary>
    public static MailboxAddress Remitente(string from, string? usuarioSmtp)
    {
        var configurado = MailboxAddress.Parse(from);
        return configurado.Address.EndsWith("@resend.dev", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(usuarioSmtp)
            ? new MailboxAddress(configurado.Name ?? "CashPyme", usuarioSmtp)
            : configurado;
    }
}
