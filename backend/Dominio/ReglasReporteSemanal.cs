namespace Backend.Dominio;

/// <summary>
/// Cuándo le toca el resumen semanal a una empresa: el día y desde la hora configurados, en su
/// propia zona horaria, y sin otro reporte en los últimos 6 días (así un worker que corre cada
/// hora no lo manda dos veces el mismo lunes). Antes era un WHERE con EXTRACT(ISODOW …) en SQL.
/// </summary>
public static class ReglasReporteSemanal
{
    /// <param name="diaIso">1 = lunes … 7 = domingo.</param>
    public static bool TocaEnviar(
        string? zonaHoraria,
        DateTime? ultimoReporteUtc,
        DateTime ahoraUtc,
        int diaIso,
        int horaDesde)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc), ZonasHorarias.Resolver(zonaHoraria));
        var diaDeHoy = local.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)local.DayOfWeek;

        return diaDeHoy == diaIso
               && local.Hour >= horaDesde
               && (ultimoReporteUtc is null || ultimoReporteUtc < ahoraUtc.AddDays(-6));
    }
}
