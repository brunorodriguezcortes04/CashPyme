namespace Backend.Dominio;

/// <summary>Una fila del flujo diario: lo que entró y salió de la caja ese día.</summary>
public record FlujoDia(DateOnly Fecha, decimal Ingresos, decimal Egresos);

/// <summary>Totales del dashboard de caja.</summary>
public record TotalesCaja(decimal SaldoTotal, decimal Ingresos, decimal Egresos, decimal FlujoNeto);

/// <summary>
/// Dashboard de caja actual. Los totales del período salen de sumar el flujo diario, así la
/// tarjeta y la tabla por día no pueden contar cosas distintas.
/// </summary>
public static class CajaActual
{
    /// <summary>
    /// Mes pedido, o el mes en curso si no se pide ninguno. Null si el pedido no es válido.
    /// </summary>
    public static (DateOnly Desde, DateOnly Hasta)? Periodo(int? anio, int? mes, DateOnly hoy)
    {
        if (anio is null && mes is null)
        {
            return ReglasMovimiento.MesEnCurso(hoy);
        }

        if (anio is null or < 1 or > 9999 || mes is null or < 1 or > 12)
        {
            return null;
        }

        var desde = new DateOnly(anio.Value, mes.Value, 1);
        return (desde, desde.AddMonths(1).AddDays(-1));
    }

    /// <summary>El saldo total es la suma de las cuentas activas que recibe.</summary>
    public static TotalesCaja Calcular(IEnumerable<decimal> saldosCuentasActivas, IEnumerable<FlujoDia> flujoDiario)
    {
        var dias = flujoDiario.ToList();
        var ingresos = dias.Sum(d => d.Ingresos);
        var egresos = dias.Sum(d => d.Egresos);
        return new TotalesCaja(saldosCuentasActivas.Sum(), ingresos, egresos, ingresos - egresos);
    }
}