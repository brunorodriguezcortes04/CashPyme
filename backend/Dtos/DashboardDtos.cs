using Backend.Dominio;

namespace Backend.Dtos;

/// <summary>
/// Saldo neto proyectado y cómo se llegó a él: saldo en caja + por cobrar - por pagar.
/// DocumentosPorCobrar y DocumentosPorPagar cuentan solo los que aportan algo: las notas, cuyo
/// saldo propio es 0, no entran.
/// </summary>
public record SaldoNetoResponse(
    decimal SaldoCaja,
    decimal PorCobrar,
    decimal PorPagar,
    decimal SaldoNeto,
    int DocumentosPorCobrar,
    int DocumentosPorPagar);

/// <summary>
/// Dashboard de caja actual. SaldoTotal es la suma de las cuentas activas (Cuentas trae el
/// detalle); Ingresos y Egresos son del período Desde-Hasta y suman lo mismo que FlujoDiario.
/// </summary>
public record CajaActualResponse(
    DateOnly Desde,
    DateOnly Hasta,
    decimal SaldoTotal,
    decimal Ingresos,
    decimal Egresos,
    decimal FlujoNeto,
    IReadOnlyList<SaldoCuentaResponse> Cuentas,
    IReadOnlyList<FlujoDiaResponse> FlujoDiario);

public record SaldoCuentaResponse(long Id, string NombreCuenta, string TipoCuenta, decimal SaldoActual);

public record FlujoDiaResponse(DateOnly Fecha, decimal Ingresos, decimal Egresos);

/// <summary>
/// Indicadores del mes: variación de ingresos, egresos y flujo neto contra el mes anterior, y
/// las categorías que más movieron plata de cada tipo. Todo lo calcula Dominio/Indicadores.
/// </summary>
public record IndicadoresResponse(
    DateOnly Desde,
    DateOnly Hasta,
    DateOnly DesdeAnterior,
    DateOnly HastaAnterior,
    Variacion Ingresos,
    Variacion Egresos,
    Variacion FlujoNeto,
    IReadOnlyList<CategoriaRanking> TopIngresos,
    IReadOnlyList<CategoriaRanking> TopEgresos);
