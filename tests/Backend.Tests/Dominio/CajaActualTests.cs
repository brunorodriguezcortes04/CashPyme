using Backend.Dominio;

namespace Backend.Tests.Dominio;

/// <summary>Criterios de aceptación de la historia "dashboard de caja actual".</summary>
public class CajaActualTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 19);

    [Fact]
    public void Criterio_1_totales_del_periodo_coinciden_con_el_flujo_diario()
    {
        FlujoDia[] dias =
        [
            new(new DateOnly(2026, 10, 1), 100_000m, 20_000m),
            new(new DateOnly(2026, 10, 5), 0m, 35_000m),
            new(new DateOnly(2026, 10, 19), 50_000m, 0m)
        ];

        var r = CajaActual.Calcular([400_000m], dias);

        Assert.Equal(new TotalesCaja(400_000m, 150_000m, 55_000m, 95_000m), r);
    }

    [Fact]
    public void Criterio_2_el_saldo_total_suma_todas_las_cuentas()
    {
        var r = CajaActual.Calcular([300_000m, 1_200_000m, -50_000m], []);

        Assert.Equal(1_450_000m, r.SaldoTotal);
    }

    [Fact]
    public void Sin_periodo_es_el_mes_en_curso()
    {
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), CajaActual.Periodo(null, null, Hoy));
    }

    [Fact]
    public void Un_mes_pedido_va_del_primero_al_ultimo_dia()
    {
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), CajaActual.Periodo(2028, 2, Hoy));
    }

    [Theory]
    [InlineData(2026, null)]
    [InlineData(null, 10)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void Un_periodo_incompleto_o_fuera_de_rango_no_es_valido(int? anio, int? mes)
    {
        Assert.Null(CajaActual.Periodo(anio, mes, Hoy));
    }
}