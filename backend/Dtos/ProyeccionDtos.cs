namespace Backend.Dtos;

/// <summary>
/// Una proyección guardada (proyeccion_flujo_caja): cuándo se generó, con qué modelo y un
/// horizonte por cada uno que se guardó (30 y 60 días), ya resumido para el dashboard.
/// </summary>
public record ProyeccionResponse(
    DateTime FechaGeneracion,
    string ModeloUsado,
    string EtiquetaModelo,
    IReadOnlyList<HorizonteProyeccionResponse> Horizontes);

/// <summary>
/// SaldoInicial es la caja de hoy antes de lo que vence hoy; Dias va de Desde a Hasta, uno por
/// fecha. PrimerDiaNegativo es null si el saldo no baja de cero en todo el horizonte.
/// </summary>
public record HorizonteProyeccionResponse(
    int HorizonteDias,
    DateOnly Desde,
    DateOnly Hasta,
    decimal SaldoInicial,
    decimal SaldoFinal,
    decimal TotalIngresos,
    decimal TotalEgresos,
    decimal SaldoMinimo,
    DateOnly FechaSaldoMinimo,
    DateOnly? PrimerDiaNegativo,
    IReadOnlyList<DiaProyeccionResponse> Dias);

public record DiaProyeccionResponse(DateOnly Fecha, decimal Ingresos, decimal Egresos, decimal Saldo);
