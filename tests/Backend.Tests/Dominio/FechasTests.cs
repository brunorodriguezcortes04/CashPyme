using Backend.Dominio;

namespace Backend.Tests.Dominio;

/// <summary>
/// "Hoy" y el día del reporte semanal salen de la zona horaria de la empresa, no de la del
/// servidor (UTC en Render). Las fechas están lejos de los cambios de horario de Chile para que
/// la prueba no dependa de la versión de la base de zonas del sistema.
/// </summary>
public class FechasTests
{
    private static DateTime Utc(int mes, int dia, int hora, int minuto = 0)
        => new(2026, mes, dia, hora, minuto, 0, DateTimeKind.Utc);

    [Fact]
    public void Hoy_es_el_de_la_empresa_y_no_el_del_servidor()
    {
        // 03:30 UTC del 15 de julio son las 23:30 del 14 en Santiago (UTC-4 en invierno).
        Assert.Equal(new DateOnly(2026, 7, 14), ZonasHorarias.Hoy(ZonasHorarias.Chile, Utc(7, 15, 3, 30)));
        Assert.Equal(new DateOnly(2026, 7, 14), ZonasHorarias.Hoy(null, Utc(7, 15, 3, 30)));
    }

    [Fact]
    public void Una_zona_desconocida_cae_a_utc_en_vez_de_fallar()
        => Assert.Equal(new DateOnly(2026, 7, 15), ZonasHorarias.Hoy("Zona/Inexistente", Utc(7, 15, 3, 30)));

    // 12 de octubre de 2026 es lunes; en Santiago rige UTC-3.
    [Fact]
    public void El_reporte_sale_el_dia_y_desde_la_hora_configurados()
    {
        Assert.True(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, null, Utc(10, 12, 12), diaIso: 1, horaDesde: 8));
        Assert.False(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, null, Utc(10, 12, 12), diaIso: 1, horaDesde: 10));
        Assert.False(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, null, Utc(10, 13, 12), diaIso: 1, horaDesde: 8));
    }

    [Fact]
    public void El_dia_se_mira_en_la_zona_de_la_empresa()
    {
        // 02:00 UTC del martes 13 todavía es lunes 23:00 en Santiago.
        Assert.True(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, null, Utc(10, 13, 2), diaIso: 1, horaDesde: 8));
        // Domingo es 7 (ISO), no 0.
        Assert.True(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, null, Utc(10, 11, 15), diaIso: 7, horaDesde: 8));
    }

    [Fact]
    public void No_se_manda_dos_veces_la_misma_semana()
    {
        var ahora = Utc(10, 12, 12);

        Assert.False(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, ahora.AddHours(-1), ahora, 1, 8));
        Assert.False(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, ahora.AddDays(-3), ahora, 1, 8));
        Assert.True(ReglasReporteSemanal.TocaEnviar(ZonasHorarias.Chile, ahora.AddDays(-7), ahora, 1, 8));
    }

    [Fact]
    public void Cada_tipo_de_cuenta_sabe_su_flujo_y_con_quien_se_registra()
    {
        Assert.Equal("ingreso", TiposCuentaDocumento.FlujoDe(TiposCuentaDocumento.PorCobrar));
        Assert.Equal("egreso", TiposCuentaDocumento.FlujoDe(TiposCuentaDocumento.PorPagar));
        Assert.Equal(["cliente", "ambos"], TiposCuentaDocumento.TiposTerceroQueSirven(TiposCuentaDocumento.PorCobrar));
        Assert.Equal(["proveedor", "ambos"], TiposCuentaDocumento.TiposTerceroQueSirven(TiposCuentaDocumento.PorPagar));
        Assert.Equal("Proveedor", TiposCuentaDocumento.Pantalla(TiposCuentaDocumento.PorPagar).Tercero);
        Assert.Equal("Cliente", TiposCuentaDocumento.Pantalla(TiposCuentaDocumento.PorCobrar).Tercero);
    }
}
