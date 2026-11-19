namespace Backend.Models;

/// <summary>
/// Un día de una proyección de caja ya generada. Tabla: proyeccion_flujo_caja (migración 019).
/// Las filas de una misma generación comparten CompanyId + GeneratedAtUtc, con un juego por
/// horizonte. Las escribe solo ProyeccionFlujoCajaService al generar; no se editan.
/// </summary>
public class ProyeccionFlujoCaja
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public short HorizonDays { get; set; }
    public DateOnly ProjectedDate { get; set; }
    public decimal ProjectedIncome { get; set; }
    public decimal ProjectedExpense { get; set; }

    /// <summary>Saldo de caja proyectado al cierre de ProjectedDate (monto_proyectado).</summary>
    public decimal ProjectedBalance { get; set; }

    public required string Model { get; set; }
}
