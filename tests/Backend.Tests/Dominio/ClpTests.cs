using System.Globalization;
using Backend.Dominio;

namespace Backend.Tests.Dominio;

public class ClpTests
{
    [Theory]
    [InlineData(119000, "$119.000")]
    [InlineData(-50000, "-$50.000")]
    [InlineData(0, "$0")]
    [InlineData(1234567.6, "$1.234.568")]
    [InlineData(999, "$999")]
    public void Formatea_con_punto_de_miles_y_sin_decimales(decimal monto, string esperado)
        => Assert.Equal(esperado, Clp.Formatear(monto));

    [Fact]
    public void Un_negativo_que_redondea_a_cero_no_lleva_signo()
        => Assert.Equal("$0", Clp.Formatear(-0.4m));

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-CL")]
    [InlineData("de-DE")]
    public void No_depende_de_la_cultura_del_servidor(string cultura)
    {
        var anterior = CultureInfo.CurrentCulture;
        try
        {
            // En Render (Linux, en-US) un ":N0" saldría "$119,000", que en Chile se lee como 119 pesos.
            CultureInfo.CurrentCulture = new CultureInfo(cultura);
            Assert.Equal("$119.000", Clp.Formatear(119000));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }
}
