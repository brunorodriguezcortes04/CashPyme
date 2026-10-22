using Backend.Dominio;

namespace Backend.Tests.Dominio;

/// <summary>Criterios de aceptación de la historia "indicadores del dashboard".</summary>
public class IndicadoresTests
{
    [Fact]
    public void Criterio_1_la_variacion_es_actual_menos_anterior_sobre_anterior()
    {
        // (150.000 − 120.000) / 120.000 = 25 %
        var v = Indicadores.Calcular(150_000m, 120_000m, masEsMejor: true);

        Assert.Equal(new Variacion(150_000m, 120_000m, 30_000m, 25.0m, true), v);
    }

    [Fact]
    public void Una_baja_da_porcentaje_negativo()
        => Assert.Equal(-40.0m, Indicadores.Calcular(60_000m, 100_000m, masEsMejor: true).Porcentaje);

    [Fact]
    public void El_porcentaje_se_redondea_a_un_decimal()
        // 10.000 / 30.000 = 33,333… %
        => Assert.Equal(33.3m, Indicadores.Calcular(40_000m, 30_000m, masEsMejor: true).Porcentaje);

    [Fact]
    public void Sin_mes_anterior_no_hay_porcentaje()
    {
        var v = Indicadores.Calcular(50_000m, 0m, masEsMejor: true);

        Assert.Null(v.Porcentaje);
        Assert.Null(v.Favorable);
        Assert.Equal(50_000m, v.Diferencia);
    }

    [Fact]
    public void Mas_egresos_es_desfavorable_y_mas_ingresos_favorable()
    {
        Assert.False(Indicadores.Calcular(120m, 100m, masEsMejor: false).Favorable);
        Assert.True(Indicadores.Calcular(80m, 100m, masEsMejor: false).Favorable);
        Assert.True(Indicadores.Calcular(120m, 100m, masEsMejor: true).Favorable);
        Assert.Null(Indicadores.Calcular(100m, 100m, masEsMejor: true).Favorable);
    }

    [Fact]
    public void Un_flujo_neto_que_mejora_desde_negativo_sube()
        // De −100.000 a −50.000: mejoró la mitad, no "bajó 50 %".
        => Assert.Equal(50.0m, Indicadores.Calcular(-50_000m, -100_000m, masEsMejor: true).Porcentaje);

    [Fact]
    public void Criterio_2_el_ranking_va_de_mayor_a_menor_monto()
    {
        var ranking = Indicadores.Ranking(
        [
            new(1, "Arriendo", 300_000m, 1),
            new(2, "Proveedores", 750_000m, 4),
            new(3, "Servicios básicos", 45_000m, 3),
            new(4, "Remuneraciones", 900_000m, 2)
        ]);

        Assert.Equal(["Remuneraciones", "Proveedores", "Arriendo", "Servicios básicos"],
            ranking.Select(c => c.NombreCategoria));
        Assert.Equal([1, 2, 3, 4], ranking.Select(c => c.Posicion));
        // 900.000 de 1.995.000 = 45,1 %
        Assert.Equal(45.1m, ranking[0].Participacion);
    }

    [Fact]
    public void El_ranking_muestra_las_primeras_cinco_y_desempata_por_nombre()
    {
        var ranking = Indicadores.Ranking(Enumerable.Range(1, 7)
            .Select(i => new MontoCategoria(i, $"Cat {8 - i}", i <= 2 ? 100m : i * 10m, 1)));

        Assert.Equal(Indicadores.TamanoRanking, ranking.Count);
        // Las dos de 100 empatan: primero la de nombre menor ("Cat 6" antes que "Cat 7").
        Assert.Equal(["Cat 6", "Cat 7"], ranking.Take(2).Select(c => c.NombreCategoria));
    }

    [Fact]
    public void El_mes_anterior_es_el_mes_calendario_completo()
    {
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), Indicadores.MesAnterior(new DateOnly(2026, 3, 1)));
        Assert.Equal((new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)), Indicadores.MesAnterior(new DateOnly(2026, 1, 1)));
    }
}
