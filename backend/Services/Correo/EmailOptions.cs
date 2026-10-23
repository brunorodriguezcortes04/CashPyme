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

    /// <summary>
    /// Envío por SMTP (Gmail, Brevo...). Si está configurado tiene prioridad sobre Resend: sirve
    /// para entregar a cualquier dirección sin verificar un dominio. Ver SmtpEmailSender.
    /// </summary>
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>
/// Sección "Email:Smtp". La contraseña NO va en el repo:
///   dev:  dotnet user-secrets set "Email:Smtp:Password" "..."
///   prod: variable de entorno Email__Smtp__Password
/// Gmail: Host smtp.gmail.com, Port 587, Usuario la dirección de Gmail y Password una
/// "contraseña de aplicación" (requiere verificación en 2 pasos), no la contraseña de la cuenta.
/// </summary>
public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Usuario { get; set; }
    public string? Password { get; set; }

    public bool Configurado => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Sección "App": datos de la aplicación que los correos necesitan para armar enlaces.</summary>
public class AppOptions
{
    /// <summary>URL pública del frontend, sin "/" final. Los enlaces de los correos apuntan acá.</summary>
    public string FrontendUrl { get; set; } = "http://localhost:4200";
}
