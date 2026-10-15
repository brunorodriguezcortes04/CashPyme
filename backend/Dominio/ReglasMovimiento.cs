using Backend.Models;

namespace Backend.Dominio;

/// <summary>Botones que la pantalla muestra en la fila de un movimiento.</summary>
public record AccionesMovimiento(bool PuedeEditar, bool PuedeAnular);

/// <summary>
/// Reglas de un movimiento de caja que la pantalla necesita conocer. Viven acá para que el front
/// solo muestre lo que recibe, igual que con los documentos.
/// </summary>
public static class ReglasMovimiento
{
    /// <summary>
    /// Un anulado no admite nada. Uno con pagos aplicados no se edita (MovimientosService lo
    /// rechaza siempre: cambiar el monto rompería lo que dicen esos pagos), pero sí se anula,
    /// previa confirmación.
    /// </summary>
    public static AccionesMovimiento Acciones(string estado, bool tienePagosAplicados)
        => estado == MovimientoFinanciero.Anulado
            ? new AccionesMovimiento(false, false)
            : new AccionesMovimiento(PuedeEditar: !tienePagosAplicados, PuedeAnular: true);

    public static string EtiquetaEstado(string estado)
        => estado == MovimientoFinanciero.Anulado ? "Anulado" : "Registrado";

    /// <summary>
    /// El MVP no bloquea el sobregiro: el movimiento se guarda igual. Esto avisa en el momento,
    /// y no al revisar la caja más tarde, que la cuenta quedó en negativo.
    /// </summary>
    public static string? AdvertenciaSobregiro(string nombreCuenta, decimal saldoCuenta)
        => saldoCuenta < 0
            ? $"La cuenta \"{nombreCuenta}\" quedó con saldo negativo. El movimiento se guardó igual."
            : null;

    /// <summary>Período que muestra el listado si no se pide otro: el mes en curso de la empresa.</summary>
    public static (DateOnly Desde, DateOnly Hasta) MesEnCurso(DateOnly hoy)
    {
        var desde = new DateOnly(hoy.Year, hoy.Month, 1);
        return (desde, desde.AddMonths(1).AddDays(-1));
    }
}
