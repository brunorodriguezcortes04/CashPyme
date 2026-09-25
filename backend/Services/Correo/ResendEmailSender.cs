using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Backend.Services.Correo;

/// <summary>
/// Envía por la API HTTP de Resend (POST https://api.resend.com/emails). Se usa HttpClient
/// directo en vez del SDK: es un solo endpoint y así no se suma otra dependencia.
/// </summary>
public class ResendEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<ResendEmailSender> logger)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    // reply_to va solo si está configurado: Resend no necesita recibir el campo en null.
    private static readonly JsonSerializerOptions SinNulos = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task EnviarAsync(CorreoSaliente correo, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from = _options.From,
                to = new[] { correo.Para },
                reply_to = string.IsNullOrWhiteSpace(_options.ReplyTo) ? null : _options.ReplyTo,
                subject = correo.Asunto,
                html = correo.Html,
                text = correo.Texto
            }, options: SinNulos)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new EmailNoEnviadoException("No se pudo contactar a Resend.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // El cuerpo de Resend explica el motivo (dominio no verificado, destinatario no
                // permitido en modo prueba, límite de envío...). Se loguea, no se muestra al usuario.
                var detalle = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new EmailNoEnviadoException(
                    $"Resend respondió {(int)response.StatusCode}: {detalle}");
            }

            logger.LogInformation("Correo \"{Asunto}\" enviado a {Para}.", correo.Asunto, correo.Para);
        }
    }
}
