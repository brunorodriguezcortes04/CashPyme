namespace Backend.Models;

/// <summary>
/// "Qué pasaría si" sobre la proyección de caja. Tablas: escenario y escenario_ajuste. Hoy la API
/// solo los lee para proyectar con un escenario.
/// </summary>
public class Escenario
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string Name { get; set; }
}

/// <summary>
/// 'flujo_extra': un ingreso o egreso hipotético en una fecha.
/// 'retraso_cobros': todos los cobros llegan N días más tarde.
/// </summary>
public class EscenarioAjuste
{
    public const string FlujoExtra = "flujo_extra";
    public const string RetrasoCobros = "retraso_cobros";

    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long ScenarioId { get; set; }
    public required string Type { get; set; }
    public string? FlowType { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? Amount { get; set; }
    public short? DelayDays { get; set; }
}
