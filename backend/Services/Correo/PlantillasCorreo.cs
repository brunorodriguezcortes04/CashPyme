using System.Globalization;
using System.Net;
using System.Text;

namespace Backend.Services.Correo;

/// <summary>
/// Arma el HTML y el texto plano de cada correo. Todo lo que viene de datos del usuario pasa
/// por HtmlEncode: el nombre de una empresa o de un tercero no puede inyectar HTML.
/// Los estilos van en línea porque la mayoría de los clientes de correo ignoran &lt;style&gt;.
/// </summary>
public static class PlantillasCorreo
{
    private static readonly CultureInfo EsCl = CultureInfo.GetCultureInfo("es-CL");

    public static CorreoSaliente VerificacionEmail(string para, string nombre, string enlace, int horasVigencia) =>
        Crear(para,
            asunto: "Confirma tu correo en CashPyme",
            titulo: "Confirma tu correo",
            parrafos:
            [
                $"Hola {nombre}, gracias por crear tu cuenta en CashPyme.",
                "Confirma que este correo es tuyo para que podamos enviarte alertas de vencimientos y el resumen semanal de tu caja."
            ],
            boton: ("Confirmar correo", enlace),
            nota: $"El enlace vence en {horasVigencia} horas. Si no creaste esta cuenta, ignora este mensaje.");

    public static CorreoSaliente RecuperarPassword(string para, string nombre, string enlace, int minutosVigencia) =>
        Crear(para,
            asunto: "Restablece tu contraseña de CashPyme",
            titulo: "Restablece tu contraseña",
            parrafos:
            [
                $"Hola {nombre}, recibimos una solicitud para restablecer la contraseña de tu cuenta.",
                "Haz clic en el botón para elegir una contraseña nueva."
            ],
            boton: ("Elegir contraseña nueva", enlace),
            nota: $"El enlace vence en {minutosVigencia} minutos y sirve una sola vez. Si no pediste este cambio, ignora este correo: tu contraseña actual sigue funcionando.");

    public static CorreoSaliente PasswordCambiada(string para, string nombre, string enlaceRecuperar) =>
        Crear(para,
            asunto: "Tu contraseña de CashPyme cambió",
            titulo: "Tu contraseña cambió",
            parrafos:
            [
                $"Hola {nombre}, te avisamos que la contraseña de tu cuenta se acaba de cambiar.",
                "Si fuiste tú, no necesitas hacer nada."
            ],
            boton: ("No fui yo: recuperar mi cuenta", enlaceRecuperar),
            nota: "Enviamos este aviso cada vez que cambia la contraseña, para que nadie pueda hacerlo sin que te enteres.");

    public static CorreoSaliente Alertas(
        string para, string razonSocial, IReadOnlyList<(string Severidad, string Mensaje)> alertas, string enlace)
    {
        var criticas = alertas.Count(a => a.Severidad == "critica");
        var asunto = criticas > 0
            ? $"⚠ {criticas} alerta(s) crítica(s) en {razonSocial}"
            : $"{alertas.Count} alerta(s) nueva(s) en {razonSocial}";

        var html = new StringBuilder();
        html.Append("<ul style=\"margin:0 0 20px;padding:0;list-style:none\">");
        foreach (var (severidad, mensaje) in alertas)
        {
            var (color, etiqueta) = severidad switch
            {
                "critica" => ("#d93025", "Crítica"),
                "advertencia" => ("#b26a00", "Advertencia"),
                _ => ("#1a73e8", "Info")
            };
            html.Append($"""
                <li style="margin:0 0 10px;padding:12px 14px;border-left:4px solid {color};background:#f6f8fb;border-radius:6px">
                  <strong style="color:{color};font-size:12px;text-transform:uppercase;letter-spacing:.04em">{etiqueta}</strong><br>
                  <span style="color:#1f2933;font-size:15px">{H(mensaje)}</span>
                </li>
                """);
        }
        html.Append("</ul>");

        var texto = string.Join("\n", alertas.Select(a => $"- [{a.Severidad}] {a.Mensaje}"));

        return Crear(para, asunto,
            titulo: $"Alertas de {razonSocial}",
            parrafos: ["CashPyme detectó lo siguiente en el flujo de caja de tu empresa:"],
            boton: ("Revisar en CashPyme", enlace),
            nota: "Recibes este correo porque eres Administrador o Contador de la empresa.",
            htmlExtra: html.ToString(),
            textoExtra: texto);
    }

    public static CorreoSaliente ReporteSemanal(string para, string razonSocial, ResumenSemanal r, string enlace)
    {
        var filas = new (string Etiqueta, decimal Valor, bool Destacar)[]
        {
            ("Saldo actual en cuentas", r.SaldoActual, true),
            ("Ingresos últimos 7 días", r.Ingresos7Dias, false),
            ("Egresos últimos 7 días", r.Egresos7Dias, false),
            ("Por cobrar próximos 7 días", r.PorCobrar7Dias, false),
            ("Por pagar próximos 7 días", r.PorPagar7Dias, false),
            ("Cobros vencidos", r.CobrosVencidos, false),
            ("Pagos vencidos", r.PagosVencidos, false),
            ("Saldo proyectado a 30 días", r.SaldoProyectado30Dias, true),
            ("Saldo mínimo proyectado (30 días)", r.SaldoMinimo30Dias, false)
        };

        var html = new StringBuilder();
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 20px;border-collapse:collapse\">");
        foreach (var (etiqueta, valor, destacar) in filas)
        {
            var color = valor < 0 ? "#d93025" : "#1f2933";
            var peso = destacar ? "700" : "500";
            html.Append($"""
                <tr>
                  <td style="padding:10px 0;border-bottom:1px solid #e4e7eb;color:#52606d;font-size:14px">{etiqueta}</td>
                  <td style="padding:10px 0;border-bottom:1px solid #e4e7eb;color:{color};font-size:15px;font-weight:{peso};text-align:right;white-space:nowrap">{Pesos(valor)}</td>
                </tr>
                """);
        }
        html.Append("</table>");

        var parrafos = new List<string> { $"Este es el resumen de la caja de {razonSocial} para esta semana." };
        if (r.SaldoMinimo30Dias < 0)
        {
            parrafos.Add("Atención: la proyección muestra que la caja podría quedar en negativo durante los próximos 30 días.");
        }
        if (r.AlertasPendientes > 0)
        {
            parrafos.Add($"Tienes {r.AlertasPendientes} alerta(s) pendiente(s) de revisar.");
        }

        var texto = string.Join("\n", filas.Select(f => $"{f.Etiqueta}: {Pesos(f.Valor)}"));

        return Crear(para,
            asunto: $"Resumen semanal de caja — {razonSocial}",
            titulo: "Tu resumen semanal",
            parrafos: parrafos,
            boton: ("Ver proyección completa", enlace),
            nota: "Recibes este resumen todos los lunes porque eres Administrador o Contador de la empresa.",
            htmlExtra: html.ToString(),
            textoExtra: texto);
    }

    // El signo se pone a mano: según el sistema operativo "C0" da "-$1.000" o "$-1.000".
    private static string Pesos(decimal valor)
        => (valor < 0 ? "-" : "") + Math.Abs(valor).ToString("C0", EsCl);

    private static string H(string texto) => WebUtility.HtmlEncode(texto);

    private static CorreoSaliente Crear(
        string para,
        string asunto,
        string titulo,
        IEnumerable<string> parrafos,
        (string Texto, string Url) boton,
        string nota,
        string htmlExtra = "",
        string textoExtra = "")
    {
        var listaParrafos = parrafos.ToList();
        var htmlParrafos = string.Concat(listaParrafos.Select(p =>
            $"<p style=\"margin:0 0 16px;color:#323f4b;font-size:15px;line-height:1.6\">{H(p)}</p>"));

        var html = $"""
            <!doctype html>
            <html lang="es">
            <body style="margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef2f7;padding:32px 16px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden">
                    <tr><td style="padding:22px 32px;background:#0b1220;color:#ffffff;font-size:20px;font-weight:700">
                      Cash<span style="color:#5b9dff">Pyme</span>
                    </td></tr>
                    <tr><td style="padding:32px">
                      <h1 style="margin:0 0 20px;color:#0b1220;font-size:22px">{H(titulo)}</h1>
                      {htmlParrafos}
                      {htmlExtra}
                      <p style="margin:8px 0 24px">
                        <a href="{H(boton.Url)}" style="display:inline-block;padding:12px 22px;border-radius:8px;background:#2b6be0;color:#ffffff;font-weight:600;font-size:15px;text-decoration:none">{H(boton.Texto)}</a>
                      </p>
                      <p style="margin:0 0 8px;color:#7b8794;font-size:13px;line-height:1.5">Si el botón no funciona, copia este enlace en tu navegador:<br><a href="{H(boton.Url)}" style="color:#2b6be0;word-break:break-all">{H(boton.Url)}</a></p>
                      <p style="margin:16px 0 0;color:#7b8794;font-size:13px;line-height:1.5">{H(nota)}</p>
                    </td></tr>
                  </table>
                  <p style="margin:16px 0 0;color:#9aa5b1;font-size:12px">CashPyme · Gestión de flujo de caja para pymes</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var texto = new StringBuilder();
        texto.AppendLine(titulo).AppendLine();
        foreach (var p in listaParrafos) texto.AppendLine(p).AppendLine();
        if (textoExtra.Length > 0) texto.AppendLine(textoExtra).AppendLine();
        texto.AppendLine($"{boton.Texto}: {boton.Url}").AppendLine();
        texto.AppendLine(nota);

        return new CorreoSaliente(para, asunto, html, texto.ToString());
    }
}

/// <summary>Cifras del reporte semanal (ver NotificacionesService.ObtenerResumenAsync).</summary>
public class ResumenSemanal
{
    public decimal SaldoActual { get; set; }
    public decimal Ingresos7Dias { get; set; }
    public decimal Egresos7Dias { get; set; }
    public decimal PorCobrar7Dias { get; set; }
    public decimal PorPagar7Dias { get; set; }
    public decimal CobrosVencidos { get; set; }
    public decimal PagosVencidos { get; set; }
    public decimal SaldoProyectado30Dias { get; set; }
    public decimal SaldoMinimo30Dias { get; set; }
    public int AlertasPendientes { get; set; }
}
