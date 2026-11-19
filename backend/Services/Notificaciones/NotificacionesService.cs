using Backend.Data;
using Backend.Dominio;
using Backend.Models;
using Backend.Services.Correo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services.Notificaciones;

/// <summary>Sección "Notificaciones" de la configuración.</summary>
public class NotificacionesOptions
{
    /// <summary>
    /// Apagado por defecto a propósito: el equipo comparte la misma base de Supabase, y si
    /// cada integrante levanta la API con el worker encendido, cada uno mandaría sus propias
    /// alertas. Se enciende en UN solo lugar (producción, o la máquina de quien esté probando).
    /// </summary>
    public bool Habilitado { get; set; }

    /// <summary>
    /// Cada cuántos minutos se generan y envían alertas. 1: una alerta nueva llega al correo en
    /// menos de un minuto (el ciclo es barato y solo manda las que aún no salieron).
    /// </summary>
    public int IntervaloMinutos { get; set; } = 1;

    /// <summary>Día del reporte semanal en formato ISO: 1 = lunes … 7 = domingo.</summary>
    public int DiaReporteSemanal { get; set; } = 1;

    /// <summary>Desde qué hora (de la zona horaria de cada empresa) se manda el reporte.</summary>
    public int HoraReporteSemanal { get; set; } = 8;
}

/// <summary>
/// Correos automáticos del flujo de caja: alertas (vencimientos, saldo bajo, caja negativa
/// proyectada) y el resumen semanal. Las alertas las genera AlertasService y las cifras salen de
/// SaldosService y ProyeccionCajaService: acá solo se reparten por correo.
/// </summary>
public class NotificacionesService(
    AppDbContext db,
    AlertasService alertasService,
    SaldosService saldos,
    ProyeccionCajaService proyeccion,
    IEmailSender emailSender,
    TimeProvider reloj,
    IOptions<AppOptions> appOptions,
    IOptions<NotificacionesOptions> options,
    ILogger<NotificacionesService> logger)
{
    // Resend limita la cantidad de envíos por segundo en el plan gratuito.
    private static readonly TimeSpan PausaEntreEnvios = TimeSpan.FromMilliseconds(600);

    private static readonly string[] RolesQueReciben = [Role.Administrator, Role.Accountant];

    private string FrontendUrl => appOptions.Value.FrontendUrl.TrimEnd('/');

    // ------------------------------------------------------------------ alertas

    /// <summary>
    /// Genera las alertas (el job es idempotente) y envía por correo las que aún no se enviaron,
    /// un correo por alerta. Devuelve cuántas alertas se enviaron.
    /// </summary>
    public async Task<int> ProcesarAlertasAsync(CancellationToken ct = default)
    {
        var creadas = await alertasService.GenerarAsync();
        if (creadas > 0) logger.LogInformation("El job de alertas creó {Cantidad} alerta(s).", creadas);

        var pendientes = await (
            from a in db.Alertas
            join e in db.Companies on a.CompanyId equals e.Id
            join d in db.DocumentosFinancieros on a.DocumentId equals d.Id into documentos
            from d in documentos.DefaultIfEmpty()
            where a.Status == Alerta.Pendiente && a.EmailSentAtUtc == null && e.IsActive
            orderby a.CompanyId,
                    a.Severity == SeveridadesAlerta.Critica ? 0 : a.Severity == SeveridadesAlerta.Advertencia ? 1 : 2,
                    a.GeneratedAtUtc
            select new { a.Id, a.CompanyId, e.LegalName, a.Type, a.Severity, a.Message, TipoCuenta = d == null ? null : d.AccountType }
        ).ToListAsync(ct);

        var enviadas = 0;
        foreach (var grupo in pendientes.GroupBy(a => a.CompanyId))
        {
            var destinatarios = await DestinatariosAsync(grupo.Key, ct);

            foreach (var alerta in grupo)
            {
                // El botón lleva a la pantalla donde se resuelve, la misma que el "Ver" de la web.
                var ruta = ReglasAlertas.RutaDondeSeResuelve(alerta.Type, alerta.TipoCuenta);
                var enlace = ruta is null ? $"{FrontendUrl}/backoffice" : $"{FrontendUrl}/backoffice/{ruta}";

                var (_, reintentar) = await EnviarATodosAsync(destinatarios,
                    para => PlantillasCorreo.Alerta(para, alerta.LegalName, alerta.Severity, alerta.Message, enlace), ct);

                // Se marca salvo que convenga reintentar (ninguno salió por una falla temporal):
                // sin destinatarios o con rechazos permanentes se reintentaría para siempre.
                if (!reintentar)
                {
                    var ahora = reloj.GetUtcNow().UtcDateTime;
                    await db.Alertas
                        .Where(a => a.Id == alerta.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.EmailSentAtUtc, ahora), ct);
                    enviadas++;
                }
            }
        }

        return enviadas;
    }

    // ------------------------------------------------------------------ reporte semanal

    /// <summary>
    /// Envía el resumen semanal a las empresas a las que les toca (el día y hora configurados,
    /// en su propia zona horaria, y sin reporte en los últimos 6 días). Con idEmpresaForzada
    /// se manda a esa empresa sin mirar día ni hora (endpoint de desarrollo).
    /// </summary>
    public async Task<int> ProcesarReportesSemanalesAsync(long? idEmpresaForzada = null, CancellationToken ct = default)
    {
        var o = options.Value;
        var ahoraUtc = reloj.GetUtcNow().UtcDateTime;

        var candidatas = await db.Companies
            .Where(c => c.IsActive && (idEmpresaForzada == null || c.Id == idEmpresaForzada))
            .Select(c => new { c.Id, c.LegalName, c.TimeZone, c.LastReportAtUtc })
            .ToListAsync(ct);

        // Forzada (endpoint de desarrollo) se manda sin mirar día ni hora.
        var empresas = idEmpresaForzada is not null
            ? candidatas
            : candidatas.Where(c => ReglasReporteSemanal.TocaEnviar(
                c.TimeZone, c.LastReportAtUtc, ahoraUtc, o.DiaReporteSemanal, o.HoraReporteSemanal)).ToList();

        var enviados = 0;
        foreach (var empresa in empresas)
        {
            var resumen = await ObtenerResumenAsync(empresa.Id, ct);
            var destinatarios = await DestinatariosAsync(empresa.Id, ct);

            var (algunoEnviado, reintentar) = await EnviarATodosAsync(destinatarios,
                d => PlantillasCorreo.ReporteSemanal(d, empresa.LegalName, resumen, $"{FrontendUrl}/backoffice"), ct);

            if (!reintentar)
            {
                await db.Companies
                    .Where(c => c.Id == empresa.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastReportAtUtc, ahoraUtc), ct);
                if (algunoEnviado) enviados++;
            }
        }

        return enviados;
    }

    /// <summary>
    /// Las cifras del resumen. "Hoy" es el de la empresa, en su zona horaria, y los saldos salen
    /// de SaldosService y ProyeccionCajaService: la misma fuente que la pantalla, así que el
    /// correo no puede decir un número distinto del que muestra la app.
    /// </summary>
    private async Task<ResumenSemanal> ObtenerResumenAsync(long idEmpresa, CancellationToken ct)
    {
        var hoy = await saldos.HoyDeEmpresaAsync(idEmpresa);
        var (ingresos, egresos) = await saldos.FlujoEntreAsync(idEmpresa, hoy.AddDays(-6), hoy);

        var filas = await db.DocumentosFinancieros
            .Where(d => d.CompanyId == idEmpresa)
            .Select(d => new { d.Id, d.AccountType, d.DueDate })
            .ToListAsync(ct);
        var saldosDoc = await saldos.DeDocumentosAsync(filas.Select(d => d.Id).ToList());
        var documentos = filas
            .Select(d => new { d.AccountType, d.DueDate, Saldo = saldosDoc[d.Id] })
            .ToList();

        decimal ProximosSieteDias(string tipoCuenta) => documentos
            .Where(d => d.AccountType == tipoCuenta
                        && d.Saldo.OutstandingAmount > 0
                        && !d.Saldo.IsOverdue
                        && d.DueDate <= hoy.AddDays(7))
            .Sum(d => d.Saldo.OutstandingAmount);

        decimal Vencido(string tipoCuenta) => documentos
            .Where(d => d.AccountType == tipoCuenta && d.Saldo.IsOverdue)
            .Sum(d => d.Saldo.OutstandingAmount);

        var dias = await proyeccion.CalcularAsync(idEmpresa, 30);

        return new ResumenSemanal
        {
            SaldoActual = await saldos.CajaDeEmpresaAsync(idEmpresa),
            Ingresos7Dias = ingresos,
            Egresos7Dias = egresos,
            PorCobrar7Dias = ProximosSieteDias(TiposCuentaDocumento.PorCobrar),
            PorPagar7Dias = ProximosSieteDias(TiposCuentaDocumento.PorPagar),
            CobrosVencidos = Vencido(TiposCuentaDocumento.PorCobrar),
            PagosVencidos = Vencido(TiposCuentaDocumento.PorPagar),
            SaldoProyectado30Dias = dias.Count > 0 ? dias[^1].SaldoProyectado : 0m,
            SaldoMinimo30Dias = dias.Count > 0 ? dias.Min(d => d.SaldoProyectado) : 0m,
            AlertasPendientes = await db.Alertas.CountAsync(a => a.CompanyId == idEmpresa && a.Status == Alerta.Pendiente, ct)
        };
    }

    // ------------------------------------------------------------------ comunes

    /// <summary>Administradores y contadores activos de la empresa: el Operador no recibe finanzas.</summary>
    private Task<List<string>> DestinatariosAsync(long idEmpresa, CancellationToken ct)
        => db.CompanyMemberships
            .Where(m => m.CompanyId == idEmpresa
                        && m.IsActive
                        && m.User.IsActive
                        && RolesQueReciben.Contains(m.Role.Name))
            .Select(m => m.User.Email)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>
    /// Un correo por persona (no todos en "Para"), para no exponer las direcciones entre sí.
    /// AlgunoEnviado: al menos uno salió. Reintentar: ninguno salió y alguna falla fue temporal;
    /// si todas fueron permanentes (o no hay destinatarios) reintentar solo repetiría el rechazo.
    /// </summary>
    private async Task<(bool AlgunoEnviado, bool Reintentar)> EnviarATodosAsync(
        IReadOnlyList<string> destinatarios, Func<string, CorreoSaliente> armar, CancellationToken ct)
    {
        var algunoEnviado = false;
        var fallaTemporal = false;
        foreach (var destinatario in destinatarios)
        {
            try
            {
                await emailSender.EnviarAsync(armar(destinatario), ct);
                algunoEnviado = true;
            }
            catch (EmailNoEnviadoException ex)
            {
                fallaTemporal |= !ex.Permanente;
                logger.LogError(ex, "No se pudo enviar una notificación a {Destinatario}.", destinatario);
            }
            await Task.Delay(PausaEntreEnvios, ct);
        }
        return (algunoEnviado, !algunoEnviado && fallaTemporal);
    }
}
