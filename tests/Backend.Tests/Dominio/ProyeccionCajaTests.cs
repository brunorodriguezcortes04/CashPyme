using Backend.Dominio;
using Backend.Models;

namespace Backend.Tests.Dominio;

public class ProyeccionCajaTests
{
    private static readonly DateOnly Hoy = new(2026, 1, 10);

    private static IReadOnlyList<DiaProyectado> Proyectar(
        decimal saldoInicial = 100m,
        int dias = 30,
        IEnumerable<DocumentoParaProyeccion>? documentos = null,
        IEnumerable<FlujoRecurrenteParaProyeccion>? recurrentes = null,
        IEnumerable<FlujoExtraParaProyeccion>? extras = null,
        int retraso = 0)
        => ProyeccionCaja.Calcular(saldoInicial, Hoy, dias, documentos ?? [], recurrentes ?? [], extras ?? [], retraso);

    private static DiaProyectado En(IReadOnlyList<DiaProyectado> p, DateOnly dia) => p.Single(d => d.Dia == dia);

    [Fact]
    public void Un_dia_por_fecha_desde_hoy_hasta_el_horizonte_inclusive()
    {
        var p = Proyectar(dias: 30);

        Assert.Equal(31, p.Count);
        Assert.Equal(Hoy, p[0].Dia);
        Assert.Equal(Hoy.AddDays(30), p[^1].Dia);
        Assert.All(p, d => Assert.Equal(100m, d.SaldoProyectado));
    }

    [Fact]
    public void Cada_documento_cae_el_dia_que_vence_y_lo_vencido_cae_hoy()
    {
        var p = Proyectar(documentos:
        [
            new(TiposCuentaDocumento.PorCobrar, Hoy.AddDays(-5), 50m),
            new(TiposCuentaDocumento.PorPagar, Hoy.AddDays(10), 30m),
            new(TiposCuentaDocumento.PorCobrar, Hoy.AddDays(60), 999m), // fuera del horizonte
            new(TiposCuentaDocumento.PorCobrar, Hoy.AddDays(3), 0m)     // sin saldo
        ]);

        Assert.Equal(50m, p[0].TotalIngresos);
        Assert.Equal(150m, p[0].SaldoProyectado);
        Assert.Equal(30m, En(p, Hoy.AddDays(10)).TotalEgresos);
        Assert.Equal(0m, En(p, Hoy.AddDays(3)).TotalIngresos);
        Assert.Equal(120m, p[^1].SaldoProyectado);
    }

    [Fact]
    public void El_retraso_de_cobros_mueve_solo_los_cobros()
    {
        var p = Proyectar(retraso: 5, documentos:
        [
            new(TiposCuentaDocumento.PorCobrar, Hoy.AddDays(2), 40m),
            new(TiposCuentaDocumento.PorPagar, Hoy.AddDays(2), 25m)
        ]);

        Assert.Equal(25m, En(p, Hoy.AddDays(2)).TotalEgresos);
        Assert.Equal(0m, En(p, Hoy.AddDays(2)).TotalIngresos);
        Assert.Equal(40m, En(p, Hoy.AddDays(7)).TotalIngresos);
    }

    [Fact]
    public void Un_flujo_recurrente_aporta_sus_repeticiones_posteriores_a_hoy()
    {
        var p = Proyectar(dias: 21, recurrentes:
        [
            // Empieza hoy: la repetición de hoy no cuenta (ya debería estar en la caja).
            new(TiposMovimiento.Ingreso, 10m, "semanal", Hoy, null)
        ]);

        Assert.Equal(0m, p[0].TotalIngresos);
        Assert.Equal(10m, En(p, Hoy.AddDays(7)).TotalIngresos);
        Assert.Equal(10m, En(p, Hoy.AddDays(14)).TotalIngresos);
        Assert.Equal(10m, En(p, Hoy.AddDays(21)).TotalIngresos);
        Assert.Equal(130m, p[^1].SaldoProyectado);
    }

    [Fact]
    public void Un_flujo_recurrente_respeta_su_fecha_de_fin()
    {
        var p = Proyectar(dias: 30, recurrentes:
        [
            new(TiposMovimiento.Egreso, 10m, "semanal", Hoy, Hoy.AddDays(15))
        ]);

        Assert.Equal(20m, p.Sum(d => d.TotalEgresos));
    }

    [Fact]
    public void Las_repeticiones_mensuales_se_cuentan_desde_el_inicio()
    {
        // Del 31 de enero: 28 de febrero y de vuelta al 31 en marzo, como "k meses" en Postgres.
        var inicio = new DateOnly(2026, 1, 31);

        Assert.Equal(new DateOnly(2026, 2, 28), ProyeccionCaja.Repeticion(inicio, "mensual", 1));
        Assert.Equal(new DateOnly(2026, 3, 31), ProyeccionCaja.Repeticion(inicio, "mensual", 2));
        Assert.Equal(new DateOnly(2027, 1, 31), ProyeccionCaja.Repeticion(inicio, "anual", 1));
        Assert.Equal(new DateOnly(2026, 2, 7), ProyeccionCaja.Repeticion(inicio, "semanal", 1));
    }

    [Fact]
    public void Los_flujos_extra_del_escenario_entran_solo_dentro_del_horizonte()
    {
        var p = Proyectar(dias: 10, extras:
        [
            new(TiposMovimiento.Egreso, Hoy.AddDays(4), 300m),
            new(TiposMovimiento.Ingreso, Hoy.AddDays(40), 1_000m)
        ]);

        Assert.Equal(-200m, En(p, Hoy.AddDays(4)).SaldoProyectado);
        Assert.Equal(-200m, p[^1].SaldoProyectado);
    }
}
