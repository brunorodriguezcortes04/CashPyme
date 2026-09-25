using Backend.Data;
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

    /// <summary>Cada cuántos minutos se generan y envían alertas.</summary>
    public int IntervaloMinutos { get; set; } = 60;

    /// <summary>Día del reporte semanal en formato ISO: 1 = lunes … 7 = domingo.</summary>
    public int DiaReporteSemanal { get; set; } = 1;

    /// <summary>Desde qué hora (de la zona horaria de cada empresa) se manda el reporte.</summary>
    public int HoraReporteSemanal { get; set; } = 8;
}

/// <summary>
/// Correos automáticos del flujo de caja: alertas (vencimientos, saldo bajo, caja negativa
/// proyectada) y el resumen semanal. Las reglas de negocio de las alertas viven en la base
/// (fn_generar_alertas); acá solo se llama a esa función y se reparte lo que produjo.
/// </summary>
public class NotificacionesService(
    AppDbContext db,
    IEmailSender emailSender,
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
    /// Genera las alertas (fn_generar_alertas es idempotente) y envía por correo las que aún
    /// no se enviaron, agrupadas en un correo por empresa. Devuelve cuántas alertas se enviaron.
    /// </summary>
    public async Task<int> ProcesarAlertasAsync(CancellationToken ct = default)
    {
        var creadas = await db.Database
            .SqlQuery<int>($"SELECT fn_generar_alertas() AS \"Value\"")
            .ToListAsync(ct);
        logger.LogInformation("fn_generar_alertas creó {Cantidad} alerta(s).", creadas.FirstOrDefault());

        var pendientes = await db.Database.SqlQuery<AlertaPorEnviar>($"""
            SELECT a.id_alerta       AS "IdAlerta",
                   a.id_empresa      AS "IdEmpresa",
                   e.razon_social    AS "RazonSocial",
                   a.nivel_severidad AS "Severidad",
                   a.mensaje         AS "Mensaje"
            FROM alerta a
            JOIN empresa e ON e.id_empresa = a.id_empresa
            WHERE a.estado_alerta = 'pendiente'
              AND a.fecha_envio_email IS NULL
              AND e.activo
            ORDER BY a.id_empresa,
                     CASE a.nivel_severidad WHEN 'critica' THEN 0 WHEN 'advertencia' THEN 1 ELSE 2 END,
                     a.fecha_generacion
            """).ToListAsync(ct);

        var enviadas = 0;
        foreach (var grupo in pendientes.GroupBy(a => (a.IdEmpresa, a.RazonSocial)))
        {
            var alertas = grupo.Select(a => (a.Severidad, a.Mensaje)).ToList();
            var destinatarios = await DestinatariosAsync(grupo.Key.IdEmpresa, ct);

            var algunoEnviado = await EnviarATodosAsync(destinatarios,
                d => PlantillasCorreo.Alertas(d, grupo.Key.RazonSocial, alertas, $"{FrontendUrl}/backoffice"), ct);

            // Sin destinatarios también se marcan: si no, se reintentarían para siempre.
            // Si todos los envíos fallaron NO se marcan, así el próximo ciclo los reintenta.
            if (algunoEnviado || destinatarios.Count == 0)
            {
                var ids = grupo.Select(a => a.IdAlerta).ToArray();
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE alerta SET fecha_envio_email = now() WHERE id_alerta = ANY({ids})", ct);
                enviadas += ids.Length;
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

        var empresas = idEmpresaForzada is { } id
            ? await db.Database.SqlQuery<EmpresaReporte>($"""
                SELECT id_empresa AS "Id", razon_social AS "RazonSocial"
                FROM empresa WHERE id_empresa = {id} AND activo
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<EmpresaReporte>($"""
                SELECT id_empresa AS "Id", razon_social AS "RazonSocial"
                FROM empresa
                WHERE activo
                  AND EXTRACT(ISODOW FROM now() AT TIME ZONE zona_horaria) = {o.DiaReporteSemanal}
                  AND EXTRACT(HOUR   FROM now() AT TIME ZONE zona_horaria) >= {o.HoraReporteSemanal}
                  AND (fecha_ultimo_reporte IS NULL OR fecha_ultimo_reporte < now() - INTERVAL '6 days')
                """).ToListAsync(ct);

        var enviados = 0;
        foreach (var empresa in empresas)
        {
            var resumen = await ObtenerResumenAsync(empresa.Id, ct);
            var destinatarios = await DestinatariosAsync(empresa.Id, ct);

            var algunoEnviado = await EnviarATodosAsync(destinatarios,
                d => PlantillasCorreo.ReporteSemanal(d, empresa.RazonSocial, resumen, $"{FrontendUrl}/backoffice"), ct);

            if (algunoEnviado || destinatarios.Count == 0)
            {
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE empresa SET fecha_ultimo_reporte = now() WHERE id_empresa = {empresa.Id}", ct);
                if (algunoEnviado) enviados++;
            }
        }

        return enviados;
    }

    /// <summary>
    /// Todas las cifras en una sola consulta. "Hoy" se calcula en la zona horaria de la
    /// empresa, igual que en vista_documento_saldo y fn_proyeccion_caja.
    /// </summary>
    private async Task<ResumenSemanal> ObtenerResumenAsync(long idEmpresa, CancellationToken ct)
    {
        var filas = await db.Database.SqlQuery<ResumenSemanal>($"""
            WITH h AS (
                SELECT (now() AT TIME ZONE zona_horaria)::date AS hoy
                FROM empresa WHERE id_empresa = {idEmpresa}
            ),
            p AS (
                SELECT dia, saldo_proyectado FROM fn_proyeccion_caja({idEmpresa}, 30, NULL)
            )
            SELECT
                (SELECT COALESCE(SUM(s.saldo_actual), 0) FROM vista_saldo_cuenta s
                  WHERE s.id_empresa = {idEmpresa} AND s.activo)                                  AS "SaldoActual",
                (SELECT COALESCE(SUM(f.total_ingresos), 0) FROM vista_flujo_diario f
                  WHERE f.id_empresa = {idEmpresa}
                    AND f.fecha_movimiento >  h.hoy - 7 AND f.fecha_movimiento <= h.hoy)          AS "Ingresos7Dias",
                (SELECT COALESCE(SUM(f.total_egresos), 0) FROM vista_flujo_diario f
                  WHERE f.id_empresa = {idEmpresa}
                    AND f.fecha_movimiento >  h.hoy - 7 AND f.fecha_movimiento <= h.hoy)          AS "Egresos7Dias",
                (SELECT COALESCE(SUM(d.saldo_pendiente), 0) FROM vista_documento_saldo d
                  WHERE d.id_empresa = {idEmpresa} AND d.tipo_cuenta = 'por_cobrar'
                    AND d.saldo_pendiente > 0 AND NOT d.esta_vencido
                    AND d.fecha_vencimiento <= h.hoy + 7)                                         AS "PorCobrar7Dias",
                (SELECT COALESCE(SUM(d.saldo_pendiente), 0) FROM vista_documento_saldo d
                  WHERE d.id_empresa = {idEmpresa} AND d.tipo_cuenta = 'por_pagar'
                    AND d.saldo_pendiente > 0 AND NOT d.esta_vencido
                    AND d.fecha_vencimiento <= h.hoy + 7)                                         AS "PorPagar7Dias",
                (SELECT COALESCE(SUM(d.saldo_pendiente), 0) FROM vista_documento_saldo d
                  WHERE d.id_empresa = {idEmpresa} AND d.tipo_cuenta = 'por_cobrar'
                    AND d.esta_vencido)                                                           AS "CobrosVencidos",
                (SELECT COALESCE(SUM(d.saldo_pendiente), 0) FROM vista_documento_saldo d
                  WHERE d.id_empresa = {idEmpresa} AND d.tipo_cuenta = 'por_pagar'
                    AND d.esta_vencido)                                                           AS "PagosVencidos",
                COALESCE((SELECT p.saldo_proyectado FROM p ORDER BY p.dia DESC LIMIT 1), 0)       AS "SaldoProyectado30Dias",
                COALESCE((SELECT MIN(p.saldo_proyectado) FROM p), 0)                              AS "SaldoMinimo30Dias",
                (SELECT COUNT(*)::int FROM alerta a
                  WHERE a.id_empresa = {idEmpresa} AND a.estado_alerta = 'pendiente')             AS "AlertasPendientes"
            FROM h
            """).ToListAsync(ct);

        return filas.FirstOrDefault() ?? new ResumenSemanal();
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
    /// Devuelve true si al menos uno salió.
    /// </summary>
    private async Task<bool> EnviarATodosAsync(
        IReadOnlyList<string> destinatarios, Func<string, CorreoSaliente> armar, CancellationToken ct)
    {
        var algunoEnviado = false;
        foreach (var destinatario in destinatarios)
        {
            try
            {
                await emailSender.EnviarAsync(armar(destinatario), ct);
                algunoEnviado = true;
            }
            catch (EmailNoEnviadoException ex)
            {
                logger.LogError(ex, "No se pudo enviar una notificación a {Destinatario}.", destinatario);
            }
            await Task.Delay(PausaEntreEnvios, ct);
        }
        return algunoEnviado;
    }

    internal sealed class AlertaPorEnviar
    {
        public long IdAlerta { get; set; }
        public long IdEmpresa { get; set; }
        public string RazonSocial { get; set; } = "";
        public string Severidad { get; set; } = "";
        public string Mensaje { get; set; } = "";
    }

    internal sealed class EmpresaReporte
    {
        public long Id { get; set; }
        public string RazonSocial { get; set; } = "";
    }
}
