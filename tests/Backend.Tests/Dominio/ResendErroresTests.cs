using System.Net;
using Backend.Services.Correo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Tests.Dominio;

/// <summary>
/// Con el ciclo de notificaciones cada minuto, un rechazo que no se arregla reintentando
/// (dirección inválida, destinatario no permitido) no puede repetirse para siempre.
/// </summary>
public class ResendErroresTests
{
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, true)] // 422: dirección inválida
    [InlineData(HttpStatusCode.Forbidden, true)]           // 403: destinatario no permitido en modo prueba
    [InlineData(HttpStatusCode.TooManyRequests, false)]    // 429: límite de envíos, pasa solo
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task Clasifica_si_el_rechazo_es_permanente(HttpStatusCode codigo, bool permanente)
    {
        var http = new HttpClient(new RespuestaFija(codigo)) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(http,
            Options.Create(new EmailOptions { ResendApiKey = "re_prueba", From = "CashPyme <a@b.cl>" }),
            NullLogger<ResendEmailSender>.Instance);

        var ex = await Assert.ThrowsAsync<EmailNoEnviadoException>(
            () => sender.EnviarAsync(new CorreoSaliente("ana@pyme.cl", "Asunto", "<p>Hola</p>", "Hola")));

        Assert.Equal(permanente, ex.Permanente);
    }

    private sealed class RespuestaFija(HttpStatusCode codigo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent("{}") });
    }
}

/// <summary>Gmail solo acepta como remitente la propia cuenta.</summary>
public class SmtpRemitenteTests
{
    [Fact]
    public void Con_el_remitente_de_prueba_de_Resend_usa_la_cuenta_smtp()
    {
        var remitente = SmtpEmailSender.Remitente("CashPyme <onboarding@resend.dev>", "cashpyme.avisos@gmail.com");
        Assert.Equal(("CashPyme", "cashpyme.avisos@gmail.com"), (remitente.Name, remitente.Address));
    }

    [Fact]
    public void Un_remitente_propio_se_respeta()
    {
        var remitente = SmtpEmailSender.Remitente("CashPyme <avisos@pyme.cl>", "usuario-brevo@smtp-brevo.com");
        Assert.Equal("avisos@pyme.cl", remitente.Address);
    }
}
