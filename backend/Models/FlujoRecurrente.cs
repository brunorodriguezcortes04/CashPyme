namespace Backend.Models;

/// <summary>
/// Ingreso o egreso que se repite (arriendo, sueldos, una venta fija). Tabla: flujo_recurrente.
/// La proyección de caja suma sus repeticiones futuras. Hoy la API solo lo lee.
/// </summary>
public class FlujoRecurrente
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string FlowType { get; set; }   // 'ingreso' | 'egreso'
    public decimal Amount { get; set; }
    public required string Frequency { get; set; }  // 'semanal' | 'mensual' | 'anual'
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}
