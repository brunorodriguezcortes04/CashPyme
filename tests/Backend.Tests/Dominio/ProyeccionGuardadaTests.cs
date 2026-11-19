using Backend.Dominio;

namespace Backend.Tests.Dominio;

/// <summary>Historia "ver una proyección de mi flujo de caja a 30 y 60 días": las reglas sin base de datos.</summary>
public class ProyeccionGuardadaTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 23);

    private static DiaProyectado Dia(int d, decimal saldo, decimal ingresos = 0m, decimal egresos = 0m)
        => new(Hoy.AddDays(d), ingresos, egresos, saldo);

    private static IReadOnlyList<DiaProyectado> Plana(int dias, decimal saldo = 100m)
        => Enumerable.Range(0, dias + 1).Select(d => Dia(d, saldo)).ToList();

    [Fact]
    public void Se_guardan_30_y_60_dias_y_se_calcula_una_vez_al_mayor()
    {
        Assert.Equal([30, 60], ProyeccionGuardada.Horizontes);
        Assert.Equal(60, ProyeccionGuardada.HorizonteMayor);
    }

    [Fact]
    public void El_horizonte_corto_es_el_comienzo_del_largo()
    {
        var sesenta = Plana(60);

        var treinta = ProyeccionGuardada.Recortar(sesenta, 30);

        Assert.Equal(31, treinta.Count);
        Assert.Equal(Hoy, treinta[0].Dia);
        Assert.Equal(Hoy.AddDays(30), treinta[^1].Dia);
        Assert.Equal(sesenta.Take(31), treinta);
        Assert.Equal(61, ProyeccionGuardada.Recortar(sesenta, 60).Count);
    }

    [Fact]
    public void No_se_inventan_dias_que_la_proyeccion_no_tiene()
        => Assert.Throws<ArgumentException>(() => ProyeccionGuardada.Recortar(Plana(30), 60));

    [Fact]
    public void Resumen_de_un_horizonte_que_cruza_a_negativo()
    {
        // Hoy entran 50 sobre una caja de 500; el día 2 se paga 900 y el día 3 entran 400.
        List<DiaProyectado> dias =
        [
            Dia(0, 550m, ingresos: 50m),
            Dia(1, 550m),
            Dia(2, -350m, egresos: 900m),
            Dia(3, 50m, ingresos: 400m),
            Dia(4, 50m)
        ];

        var r = ProyeccionGuardada.Resumir(4, dias);

        Assert.Equal((4, Hoy, Hoy.AddDays(4)), (r.HorizonteDias, r.Desde, r.Hasta));
        Assert.Equal(500m, r.SaldoInicial); // la caja antes de lo que entra hoy
        Assert.Equal(50m, r.SaldoFinal);
        Assert.Equal((450m, 900m), (r.TotalIngresos, r.TotalEgresos));
        Assert.Equal((-350m, Hoy.AddDays(2)), (r.SaldoMinimo, r.FechaSaldoMinimo));
        Assert.Equal(Hoy.AddDays(2), r.PrimerDiaNegativo);
    }

    [Fact]
    public void Resumen_sin_negativos_y_con_el_minimo_repetido()
    {
        List<DiaProyectado> dias = [Dia(0, 300m), Dia(1, 100m, egresos: 200m), Dia(2, 100m), Dia(3, 400m, ingresos: 300m)];

        var r = ProyeccionGuardada.Resumir(3, dias);

        Assert.Null(r.PrimerDiaNegativo);
        // El mínimo se repite: cuenta la primera vez que se llega a él.
        Assert.Equal((100m, Hoy.AddDays(1)), (r.SaldoMinimo, r.FechaSaldoMinimo));
        Assert.Equal(300m, r.SaldoInicial);
    }

    [Fact]
    public void Sin_proyeccion_previa_toca_generar()
        => Assert.True(ProyeccionGuardada.TocaGenerar(null, ZonasHorarias.Chile, DateTime.UtcNow));

    [Theory]
    // En octubre Chile está en UTC-3.
    // Misma fecha UTC, pero la anterior fue el 22 en Chile (23:00) y ahora ya es 23 (01:00): toca.
    [InlineData("2026-10-23T02:00:00Z", "2026-10-23T04:00:00Z", true)]
    // Distinta fecha UTC, pero las dos son el 23 en Chile (01:00 y 23:00): no toca.
    [InlineData("2026-10-23T04:00:00Z", "2026-10-24T02:00:00Z", false)]
    // Generada hace un rato el mismo día: no toca.
    [InlineData("2026-10-23T15:00:00Z", "2026-10-23T15:30:00Z", false)]
    // Generada ayer: toca.
    [InlineData("2026-10-22T15:00:00Z", "2026-10-23T15:00:00Z", true)]
    public void Una_por_dia_en_la_zona_horaria_de_la_empresa(string ultima, string ahora, bool toca)
        => Assert.Equal(toca, ProyeccionGuardada.TocaGenerar(Utc(ultima), ZonasHorarias.Chile, Utc(ahora)));

    [Fact]
    public void El_modelo_se_muestra_con_su_nombre()
    {
        Assert.Equal("Cuentas por cobrar y por pagar + flujos recurrentes",
            ModelosProyeccion.Etiqueta(ModelosProyeccion.DocumentosRecurrentes));
        Assert.Equal("otro_modelo", ModelosProyeccion.Etiqueta("otro_modelo"));
    }

    private static DateTime Utc(string iso)
        => DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
}
