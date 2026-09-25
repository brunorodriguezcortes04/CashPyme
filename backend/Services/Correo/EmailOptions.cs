namespace Backend.Services.Correo;

/// <summary>
/// Sección "Email" de la configuración. La API key NO va en el repo:
///   dev:  dotnet user-secrets set "Email:ResendApiKey" "re_..."
///   prod: variable de entorno Email__ResendApiKey
/// Sin API key la API igual arranca y los correos se escriben en la consola
/// (ConsolaEmailSender), así nadie del equipo queda bloqueado por no tener cuenta en Resend.
/// </summary>
public class EmailOptions
{
    public string? ResendApiKey { get; set; }

    /// <summary>
    /// Remitente. Mientras el dominio no esté verificado en Resend solo sirve
    /// "CashPyme &lt;onboarding@resend.dev&gt;", y en ese modo Resend únicamente entrega
    /// correos a la dirección dueña de la cuenta de Resend.
    /// </summary>
    public string From { get; set; } = "CashPyme <onboarding@resend.dev>";

    /// <summary>Dirección a la que llegan las respuestas (opcional).</summary>
    public string? ReplyTo { get; set; }
}

/// <summary>Sección "App": datos de la aplicación que los correos necesitan para armar enlaces.</summary>
public class AppOptions
{
    /// <summary>URL pública del frontend, sin "/" final. Los enlaces de los correos apuntan acá.</summary>
    public string FrontendUrl { get; set; } = "http://localhost:4200";
}
