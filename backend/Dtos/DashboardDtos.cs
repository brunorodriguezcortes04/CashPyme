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
