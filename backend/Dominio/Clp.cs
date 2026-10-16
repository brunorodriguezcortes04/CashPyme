using System.Globalization;

namespace Backend.Dominio;

/// <summary>
/// Montos en pesos chilenos para los textos que arma el backend (mensajes de alertas, correos).
/// Siempre con punto de miles: "$119.000". Con el formato de otro locale saldría "$119,000", que
/// en Chile se lee como ciento diecinueve pesos.
/// </summary>
public static class Clp
{
    public static string Formatear(decimal monto)
    {
        // Sin decimales: el CLP no los usa. Se redondea antes de mirar el signo para no escribir
        // "-$0" con montos como -0,4.
        var redondeado = Math.Round(monto, 0, MidpointRounding.AwayFromZero);
        var cifras = Math.Abs(redondeado).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
        return (redondeado < 0 ? "-$" : "$") + cifras;
    }
}
