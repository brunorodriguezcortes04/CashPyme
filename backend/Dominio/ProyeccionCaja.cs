using Backend.Models;

namespace Backend.Dominio;

/// <summary>Documento con saldo, tal como entra a la proyección.</summary>
public record DocumentoParaProyeccion(string TipoCuenta, DateOnly FechaVencimiento, decimal SaldoPendiente);

/// <summary>Ingreso o egreso que se repite (flujo_recurrente). Solo los activos entran.</summary>
public record FlujoRecurrenteParaProyeccion(
    string TipoFlujo,
    decimal Monto,
    string Frecuencia,
    DateOnly FechaInicio,
    DateOnly? FechaFin);

/// <summary>Ingreso o egreso hipotético de un escenario (escenario_ajuste 'flujo_extra').</summary>
public record FlujoExtraParaProyeccion(string TipoFlujo, DateOnly Fecha, decimal Monto);

public record DiaProyectado(DateOnly Dia, decimal TotalIngresos, decimal TotalEgresos, decimal SaldoProyectado);

/// <summary>
/// Saldo de caja día a día desde hoy hasta hoy + días. Antes era fn_proyeccion_caja; se portó sin
/// cambiar el resultado:
///   * cada documento con saldo cae el día en que vence (o hoy, si ya venció); un escenario de
///     "retraso de cobros" corre solo los cobros, no los pagos;
///   * cada flujo recurrente aporta todas sus repeticiones posteriores a hoy y dentro del
///     horizonte, sin pasar de su fecha de fin;
///   * lo que cae fuera del horizonte no entra.
/// </summary>
public static class ProyeccionCaja
{
    /// <summary>Tope de repeticiones por flujo: el mismo generate_series(0, 3000) de la función.</summary>
    public const int MaximoRepeticiones = 3000;

    public static IReadOnlyList<DiaProyectado> Calcular(
        decimal saldoInicial,
        DateOnly hoy,
        int dias,
        IEnumerable<DocumentoParaProyeccion> documentos,
        IEnumerable<FlujoRecurrenteParaProyeccion> recurrentes,
        IEnumerable<FlujoExtraParaProyeccion> extras,
        int diasRetrasoCobros = 0)
    {
        var fin = hoy.AddDays(dias);
        var ingresos = new Dictionary<DateOnly, decimal>();
        var egresos = new Dictionary<DateOnly, decimal>();

        void Sumar(DateOnly fecha, string tipo, decimal monto)
        {
            // Lo que cae fuera del calendario no entra (en la función era el LEFT JOIN al calendario).
            if (fecha < hoy || fecha > fin)
            {
                return;
            }

            var destino = tipo == TiposMovimiento.Ingreso ? ingresos : egresos;
            destino[fecha] = destino.GetValueOrDefault(fecha) + monto;
        }

        foreach (var d in documentos.Where(d => d.SaldoPendiente > 0))
        {
            var esCobro = d.TipoCuenta == TiposCuentaDocumento.PorCobrar;
            var fecha = (d.FechaVencimiento > hoy ? d.FechaVencimiento : hoy)
                .AddDays(esCobro ? diasRetrasoCobros : 0);
            Sumar(fecha, TiposCuentaDocumento.FlujoDe(d.TipoCuenta), d.SaldoPendiente);
        }

        foreach (var f in recurrentes)
        {
            for (var k = 0; k <= MaximoRepeticiones; k++)
            {
                var fecha = Repeticion(f.FechaInicio, f.Frecuencia, k);

                // Las repeticiones solo avanzan: pasado el horizonte o la fecha de fin, no vuelve a entrar ninguna.
                if (fecha > fin || (f.FechaFin is { } finFlujo && fecha > finFlujo))
                {
                    break;
                }

                if (fecha > hoy)
                {
                    Sumar(fecha, f.TipoFlujo, f.Monto);
                }
            }
        }

        foreach (var e in extras)
        {
            Sumar(e.Fecha, e.TipoFlujo, e.Monto);
        }

        var resultado = new List<DiaProyectado>(dias + 1);
        var saldo = saldoInicial;
        for (var dia = hoy; dia <= fin; dia = dia.AddDays(1))
        {
            var entra = ingresos.GetValueOrDefault(dia);
            var sale = egresos.GetValueOrDefault(dia);
            saldo += entra - sale;
            resultado.Add(new DiaProyectado(dia, entra, sale, saldo));
        }

        return resultado;
    }

    /// <summary>
    /// k-ésima repetición contada desde la fecha de inicio, no desde la anterior: un flujo
    /// mensual del 31 de enero cae el 28 de febrero y vuelve al 31 en marzo, igual que sumar
    /// "k meses" en Postgres.
    /// </summary>
    public static DateOnly Repeticion(DateOnly inicio, string frecuencia, int k) => frecuencia switch
    {
        "semanal" => inicio.AddDays(7 * k),
        "mensual" => inicio.AddMonths(k),
        _ => inicio.AddYears(k)
    };
}
