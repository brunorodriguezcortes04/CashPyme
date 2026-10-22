namespace Backend.Dominio;

/// <summary>Saldo de una cuenta activa al empezar y al cerrar el período del reporte.</summary>
public record CuentaReporte(string Nombre, string Tipo, decimal SaldoInicio, decimal SaldoCierre);

/// <summary>Un movimiento registrado del período.</summary>
public record MovimientoReporte(
    DateOnly Fecha,
    string Tipo,
    string Cuenta,
    string Categoria,
    string MedioPago,
    string? Descripcion,
    decimal Monto);

/// <summary>Un documento por cobrar o por pagar con saldo, que vence dentro del período.</summary>
public record PendienteReporte(
    string TipoCuenta,
    string Tercero,
    string Documento,
    DateOnly Vencimiento,
    decimal Saldo,
    string Estado,
    int DiasAtraso);

/// <summary>
/// Todo lo que lleva el reporte PDF. Lo arma ReportesService con los mismos cálculos que el
/// dashboard (SaldosService, CalculoDocumento) y ReportePdf solo lo dibuja.
/// </summary>
public record ReporteCaja(
    string Empresa,
    string? Rut,
    DateOnly Desde,
    DateOnly Hasta,
    DateTime GeneradoUtc,
    DateOnly GeneradoEl,
    IReadOnlyList<CuentaReporte> Cuentas,
    IReadOnlyList<MovimientoReporte> Movimientos,
    IReadOnlyList<PendienteReporte> Pendientes)
{
    public decimal SaldoInicio => Cuentas.Sum(c => c.SaldoInicio);
    public decimal SaldoCierre => Cuentas.Sum(c => c.SaldoCierre);
    public decimal Ingresos => Movimientos.Where(m => m.Tipo == TiposMovimiento.Ingreso).Sum(m => m.Monto);
    public decimal Egresos => Movimientos.Where(m => m.Tipo == TiposMovimiento.Egreso).Sum(m => m.Monto);
    public decimal PorCobrar => Pendientes.Where(p => p.TipoCuenta == TiposCuentaDocumento.PorCobrar).Sum(p => p.Saldo);
    public decimal PorPagar => Pendientes.Where(p => p.TipoCuenta == TiposCuentaDocumento.PorPagar).Sum(p => p.Saldo);
}

/// <summary>Reglas del rango de fechas del reporte.</summary>
public static class ReglasReporte
{
    /// <summary>
    /// Tope del rango: un reporte de varios años sería un PDF de cientos de páginas que nadie lee
    /// y que tarda en generarse. Un año alcanza para un cierre anual.
    /// </summary>
    public const int MaximoDias = 366;

    public enum ResultadoRango { Valido, Faltan, Invertido, MuyLargo }

    public static ResultadoRango Validar(DateOnly? desde, DateOnly? hasta)
    {
        if (desde is null || hasta is null)
        {
            return ResultadoRango.Faltan;
        }

        if (desde > hasta)
        {
            return ResultadoRango.Invertido;
        }

        return hasta.Value.DayNumber - desde.Value.DayNumber + 1 > MaximoDias
            ? ResultadoRango.MuyLargo
            : ResultadoRango.Valido;
    }

    /// <summary>Nombre del archivo descargado: reporte-caja-2026-10-01-a-2026-10-31.pdf.</summary>
    public static string NombreArchivo(DateOnly desde, DateOnly hasta)
        => $"reporte-caja-{desde:yyyy-MM-dd}-a-{hasta:yyyy-MM-dd}.pdf";
}
